using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.Flooid.Models;
using CardFactory.ProfitLoss.Flooid.Services;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Views;

public partial class FlooidView : UserControl
{
    private readonly FlooidSessionService _sessionService = new();
    private bool _initialised;
    private bool _initialising;
    private CoreWebView2Environment? _webViewEnvironment;
    private readonly List<Window> _reportPopupHosts = new();
    private Microsoft.Web.WebView2.Wpf.WebView2? _activeReportBrowser;
    // Stage 6A.55: Flooid's Product Group picker popup, tracked separately from the
    // report popup so it can be driven without becoming the report automation target.
    private Microsoft.Web.WebView2.Wpf.WebView2? _productGroupBrowser;
    // Stage 6A.59: set while the product group picker is being opened. Classifying a
    // popup purely by its initial URL fails when Flooid opens the window first and
    // navigates it afterwards (the initial URL is then about:blank), which silently
    // filed the picker as the report window and left _productGroupBrowser null.
    private bool _expectingProductGroupPicker;
    private readonly object _generatedReportCaptureLock = new();
    private string _lastGeneratedReportResponseHtml = string.Empty;
    private string _lastGeneratedReportResponseUri = string.Empty;
    // Stage 6A.63 diagnostics only. These record what the window-tracking code actually
    // did, so a report-wait timeout can say which window it followed and why, instead of
    // leaving it to be inferred from a photograph of a truncated status line.
    private string _lastPickerRetirementNote = "picker retirement has not run";
    private string _lastLeftoverDemotionNote = "leftover demotion check has not run";
    private string _lastProductGroupVerifyNote = "product group verify has not run";
    private string _lastProductGroupDrillNote = "product group drill has not run";

    public FlooidView() => InitializeComponent();

    private static bool _cookiesClearedThisRun;   // Stage 6B.63

    private static void LogStartupStep(string step)
    {
        try
        {
            AppFiles.AppendDiagnostic(
                "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - start-up: " + step + " ===\r\n");
        }
        catch { }
    }

    public event EventHandler<FlooidConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public bool IsCurrentlySignedIn { get; private set; }

    public async Task OpenAsync()
    {
        await InitialiseBrowserAsync();
        if (Browser.CoreWebView2 is not null && Browser.Source is null)
        {
            Browser.CoreWebView2.Navigate(FlooidSessionService.BackOfficeEntryUri.AbsoluteUri);
        }
    }

    /// <summary>
    /// Stage 6B.69: open the back office at its home page. Whatever the browser last showed
    /// (usually the last report of a Refresh) was laid out while the window was 860x650 and
    /// hidden; Flooid sizes its page when it loads, so shown at 90% of the screen it stayed
    /// a small block in the top-left corner (photo, 01/10). Loading it again at the new size
    /// lays it out to fill the window.
    /// </summary>
    public void OpenBackOfficeHome()
    {
        try { Browser.CoreWebView2?.Navigate(FlooidSessionService.BackOfficeEntryUri.AbsoluteUri); } catch { }
    }

    public void Refresh()
    {
        try { Browser.CoreWebView2?.Reload(); } catch { }
    }

    private async Task InitialiseBrowserAsync()
    {
        if (_initialised || _initialising) return;

        _initialising = true;
        Publish(new FlooidSessionSnapshot(
            FlooidConnectionState.NotInitialised,
            "Preparing Flooid…",
            "Starting the secure sign-in window."));

        try
        {
            Directory.CreateDirectory(_sessionService.UserDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _sessionService.UserDataFolder,
                options: new CoreWebView2EnvironmentOptions());
            _webViewEnvironment = environment;

            await Browser.EnsureCoreWebView2Async(environment);

            Browser.CoreWebView2.WebResourceResponseReceived += Browser_WebResourceResponseReceived;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            Browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            Browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;

            // Stage 6B.63: every launch starts signed out. The WebView2 profile keeps its
            // cookies on disk, so a Flooid session still alive on the server carried over
            // from the last run and the app came up already signed in. Clear them before the
            // first page is asked for - once per run, so a browser rebuilt mid-run does not
            // sign anyone out. Done at start-up rather than on exit so a crash, a kill from
            // Task Manager or a power cut starts signed out too.
            if (!_cookiesClearedThisRun)
            {
                _cookiesClearedThisRun = true;
                Browser.CoreWebView2.CookieManager.DeleteAllCookies();
                var left = -1;
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    left = (await Browser.CoreWebView2.CookieManager.GetCookiesAsync(
                        FlooidSessionService.BackOfficeEntryUri.AbsoluteUri)).Count;
                    if (left == 0) break;
                    await Task.Delay(100);
                }
                LogStartupStep("cookies cleared at launch; " + left + " left for the back office");
            }

            _initialised = true;
            BrowserErrorPanel.Visibility = Visibility.Collapsed;
            Publish(_sessionService.Evaluate(Browser.Source));
            Browser.CoreWebView2.Navigate(FlooidSessionService.BackOfficeEntryUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            ShowBrowserError("WebView2 could not be initialised. " + ex.Message);
        }
        finally
        {
            _initialising = false;
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) =>
        Publish(new FlooidSessionSnapshot(
            FlooidConnectionState.NotInitialised,
            "Opening Flooid…",
            "Waiting for the sign-in page to finish loading.",
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ? uri : null));

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Publish(new FlooidSessionSnapshot(
                FlooidConnectionState.Error,
                "Flooid page failed to load",
                $"WebView2 navigation error: {e.WebErrorStatus}.",
                Browser.Source));
            return;
        }

        Publish(_sessionService.Evaluate(Browser.Source));
    }

    private async void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        await PreserveFlooidPopupAsync(e);
    }

    private async void ReportBrowser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        await PreserveFlooidPopupAsync(e);
    }

    private async Task PreserveFlooidPopupAsync(CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (_webViewEnvironment is null)
                throw new InvalidOperationException("Flooid WebView2 environment is not ready for a report window.");

            // Stage 6A.55: Flooid's Product Group picker
            // (/backoffice/productGroupPopupSearch.action) also arrives as a popup. Every
            // popup used to be assigned to _activeReportBrowser, which is what all
            // subsequent automation scripts target - so opening the picker silently
            // redirected the automation onto the picker window, and the report wait then
            // sat on a page that would never become a report. That is the observed hang
            // on "selecting product group". The picker is now tracked separately and
            // never becomes the report automation target.
            // Stage 6A.59: trust the explicit expectation flag as well as the URL. While
            // SelectGiftCardProductGroupAsync is opening the picker, ANY popup Flooid
            // creates is the picker - it is the only thing being triggered at that moment.
            var isProductGroupPicker = _expectingProductGroupPicker
                || (e.Uri ?? string.Empty)
                    .Contains("productGroupPopupSearch.action", StringComparison.OrdinalIgnoreCase);

            var popupBrowser = new Microsoft.Web.WebView2.Wpf.WebView2();
            var popupHost = new Window
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
                Content = popupBrowser
            };

            popupHost.Show();
            await popupBrowser.EnsureCoreWebView2Async(_webViewEnvironment);
            popupBrowser.CoreWebView2.WebResourceResponseReceived += Browser_WebResourceResponseReceived;
            popupBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            popupBrowser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            popupBrowser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            popupBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            popupBrowser.CoreWebView2.NewWindowRequested += ReportBrowser_NewWindowRequested;

            // Stage 6A.60: classification above happens once, using the URL the popup was
            // CREATED with. Flooid can create a popup blank (about:blank) and navigate it
            // to the product group picker a moment later - outside the expectation window
            // that flag covers. Such a popup was filed as the report browser and nothing
            // ever corrected it, so every later automation script followed the picker
            // window instead of the criteria form. That is why the criteria form "never
            // appeared to submit" while the reported page was .../backoffice/produ...
            // Re-check on every navigation and demote any popup that turns out to be the
            // picker.
            popupBrowser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!(args.Uri ?? string.Empty).Contains("productGroupPopupSearch.action", StringComparison.OrdinalIgnoreCase))
                    return;

                if (ReferenceEquals(_activeReportBrowser, popupBrowser))
                    _activeReportBrowser = null;
                _productGroupBrowser ??= popupBrowser;
            };

            _reportPopupHosts.Add(popupHost);
            if (isProductGroupPicker)
                _productGroupBrowser = popupBrowser;
            else
                _activeReportBrowser = popupBrowser;

            // Giving Flooid a genuine CoreWebView2 new-window target preserves the opener,
            // target name, POST/navigation and /web/spring/redirect workflow that its
            // report menu expects. Do not convert the popup into a plain Navigate(uri).
            e.NewWindow = popupBrowser.CoreWebView2;
            e.Handled = true;
        }
        catch
        {
            // Stage 6A.48: never redirect a failed report popup into the signed-in primary
            // browser. Preserve the primary session/history; the report wait path will time
            // out with a diagnostic if Flooid's genuine popup cannot be created.
            e.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Browser_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri ?? string.Empty;
            if (!uri.Contains("/backoffice/htmlReportGenerator.action", StringComparison.OrdinalIgnoreCase))
                return;

            using var content = await e.Response.GetContentAsync();
            using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var html = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(html)) return;

            lock (_generatedReportCaptureLock)
            {
                _lastGeneratedReportResponseHtml = html;
                _lastGeneratedReportResponseUri = uri;
            }
        }
        catch
        {
            // DOM/frame capture remains available if response-body capture is unavailable.
        }
    }

    private void ResetGeneratedReportCapture()
    {
        lock (_generatedReportCaptureLock)
        {
            _lastGeneratedReportResponseHtml = string.Empty;
            _lastGeneratedReportResponseUri = string.Empty;
        }
    }

    private string GetCapturedGeneratedReportHtml()
    {
        lock (_generatedReportCaptureLock)
        {
            return _lastGeneratedReportResponseHtml;
        }
    }

    private static bool LooksLikeGeneratedReportHtml(string? html, string[] expectedText)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length < 200) return false;
        var lower = html.ToLowerInvariant();
        if (lower.Contains("failed to render", StringComparison.Ordinal)) return false;
        if (lower.Contains("http status 500", StringComparison.Ordinal)) return false;

        // Stage 6A.49 fix: Flooid's report *criteria* (selection) forms carry the same
        // report title, the same "Operator"/"Refunds"/"Voids" wording, and are themselves
        // laid out in HTML tables. Every check below used to accept a criteria page as a
        // finished report whenever its title text happened to match, which is exactly what
        // was captured for Team Performance (the parser then correctly rejected it for
        // missing the real 'displayTable' data table). A page that still submits to one of
        // Flooid's *CriteriaSubmit.action endpoints, or still contains an unsubmitted
        // criteria form (radio inputs alongside a submit control), is never a finished report,
        // no matter which keywords it contains.
        var isCriteriaForm = lower.Contains("criteriasubmit.action", StringComparison.Ordinal)
                           || (lower.Contains("type=\"radio\"", StringComparison.Ordinal)
                               && (lower.Contains("type=\"submit\"", StringComparison.Ordinal) || lower.Contains("type='submit'", StringComparison.Ordinal))
                               && (lower.Contains("for date", StringComparison.Ordinal) || lower.Contains("date range", StringComparison.Ordinal)));
        if (isCriteriaForm) return false;

        var hasTable = lower.Contains("<table", StringComparison.Ordinal)
                    && lower.Contains("<tr", StringComparison.Ordinal)
                    && (lower.Contains("<td", StringComparison.Ordinal) || lower.Contains("<th", StringComparison.Ordinal));
        var hasReportTotals = lower.Contains("daily total", StringComparison.Ordinal)
                           || lower.Contains("period total", StringComparison.Ordinal)
                           || lower.Contains("period summary", StringComparison.Ordinal);
        var hasOperatorReport = lower.Contains("operator", StringComparison.Ordinal)
                             && lower.Contains("sales value", StringComparison.Ordinal);
        var hasGiftCardReport = lower.Contains("600527", StringComparison.Ordinal)
                             || (lower.Contains("gift card", StringComparison.Ordinal) && lower.Contains("operator", StringComparison.Ordinal));
        var hasExpected = expectedText.Length > 0
                       && expectedText.All(term => lower.Contains(term.ToLowerInvariant(), StringComparison.Ordinal));

        // Stage 6A.49 fix: a bare keyword/title match ("hasExpected") is no longer sufficient
        // on its own — that was the specific path that let a criteria/confirmation page through
        // for Team Performance. It must now be paired with an actual results table. The other
        // three signals (report totals, operator+sales-value pairing, gift card marker) remain
        // strong enough on their own because Flooid's criteria forms don't contain them.
        return hasReportTotals || hasOperatorReport || hasGiftCardReport || (hasTable && hasExpected);
    }

    private async Task<string> GetBestRenderedReportHtmlAsync()
    {
        const string script = """
            (() => {
              const docs = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try {
                  const doc = win.document;
                  docs.push(doc);
                  for (const frame of doc.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);
              let best = document;
              let bestScore = -1;
              for (const doc of docs) {
                try {
                  const text = doc.body?.innerText || '';
                  const rows = doc.querySelectorAll('tr').length;
                  const cells = doc.querySelectorAll('td,th').length;
                  const tables = doc.querySelectorAll('table').length;
                  const score = (rows * 5000) + (cells * 500) + (tables * 10000) + text.length;
                  if (score > bestScore) { best = doc; bestScore = score; }
                } catch (_) {}
              }
              return best?.documentElement?.outerHTML || '';
            })()
            """;
        return await ExecuteStringScriptAsync(script);
    }

    private void ResetReportPopupContext()
    {
        _activeReportBrowser = null;
        _productGroupBrowser = null;
        foreach (var host in _reportPopupHosts.ToArray())
        {
            try { host.Close(); } catch { }
        }
        _reportPopupHosts.Clear();
    }

    private CoreWebView2 GetAutomationCoreWebView2()
    {
        if (_activeReportBrowser?.CoreWebView2 is not null)
            return _activeReportBrowser.CoreWebView2;
        return Browser.CoreWebView2 ?? throw new InvalidOperationException("Flooid browser is not ready.");
    }

    private Uri? GetAutomationSource()
    {
        if (_activeReportBrowser?.Source is { IsAbsoluteUri: true } popupSource)
            return popupSource;
        return Browser.Source;
    }


    public async Task<string> FetchRefundsVoidsHtmlAsync(DateTime fromDate, DateTime toDate)
    {
        var timing = new PhaseLog("Refunds/Voids");
        var succeeded = false;
        try
        {
            var html = await FetchRefundsVoidsHtmlCoreAsync(fromDate, toDate, timing);
            succeeded = true;
            return html;
        }
        finally
        {
            timing.Write(succeeded ? "ok" : "failed");
        }
    }

    private async Task<string> FetchRefundsVoidsHtmlCoreAsync(DateTime fromDate, DateTime toDate, PhaseLog timing)
    {
        await InitialiseBrowserAsync();
        EnsureReadyForAutomation();
        ResetReportPopupContext();
        ResetGeneratedReportCapture();
        timing.Mark("browser ready");

        var from = fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var to = toDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var year = toDate.Year.ToString(CultureInfo.InvariantCulture);

        // Stage 6A.52 redesign: for the everyday case - "today's" report, which is what
        // an automatic daily P&L pull actually asks for - Flooid's own pre-populated
        // default date is used as-is and nothing is written into its date fields at all.
        // This sidesteps the repeated "date is invalid" rejections entirely, since the
        // automation no longer touches the field Flooid is validating. The real date
        // Flooid used is read back out of the generated report afterwards (Metadata.
        // FromDate/ToDate), so the app always reflects what Flooid actually produced
        // rather than what was requested. A genuinely different date or range (a
        // historical day, or a weekly pull) has no way to be expressed to Flooid except
        // by writing it, so that path keeps using the hardened Stage 6A.49-6A.51 logic.
        var skipDateEntry = fromDate.Date == DateTime.Today && toDate.Date == DateTime.Today;

        await OpenReportCriteriaFromMenuAsync(
            new[] { "Refunds, Voids & No Sales Report", "Refunds, Voids & No Sales" },
            "Refunds, Voids & No Sales",
            """
            (() => {
              const text = (document.body?.innerText || '').toLowerCase();
              const hasTitle = (text.includes('refund') && text.includes('void')) || text.includes('no sales') || text.includes('no-sales');
              const hasSubmit = [...document.forms].some(f => ((f.getAttribute('action') || '') + ' ' + (f.innerText || '')).toLowerCase().includes('submit'));
              return hasTitle && hasSubmit ? 'MATCH' : 'NO';
            })()
            """,
            new[] { "Daily Reports", "Reports" });
        timing.Mark("criteria page opened");

        var configure = """
            (() => {
              const from = __FROM__;
              const to = __TO__;
              const year = __YEAR__;
              const skipDates = __SKIP_DATES__;
              const text = (document.body?.innerText || '').toLowerCase();
              if (!((text.includes('refund') && text.includes('void')) || text.includes('no sales') || text.includes('no-sales')))
                return 'ERROR: Refunds/Voids criteria page is not active.';

              const forms = [...document.forms];
              const form = forms.find(f => {
                const action = (f.getAttribute('action') || '').toLowerCase();
                const body = (f.innerText || '').toLowerCase();
                return action.includes('operatorcontrolreportcriteriasubmit.action')
                    || ((action.includes('submit.action') || action.includes('submit')) && ((body.includes('refund') && body.includes('void')) || text.includes('no sales')));
              }) || forms.find(f => (f.getAttribute('action') || '').toLowerCase().includes('submit'));
              if (!form) return 'ERROR: Refunds/Voids submit form was not found.';

              const contextFor = element => ((element.closest('tr')?.innerText || element.parentElement?.innerText || '') + ' ' + (element.name || '')).toLowerCase();
              const fire = element => {
                try { element.dispatchEvent(new Event('input', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('change', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('blur', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('keyup', {bubbles:true})); } catch (_) {}
              };
              // Stage 6A.51 fix: 6A.50 handled native <input type="date"> and simple
              // controlled-input widgets, but Flooid still rejected the date range with
              // "is invalid, a valid value is required" after that fix. Flooid's
              // .action-suffixed URLs point to an older Java back office, where date
              // fields are very often driven by a jQuery UI-style datepicker plugin that
              // keeps its OWN internal "selected date" object separate from the visible
              // text box - setting .value (even correctly, even via the native setter)
              // never touches that internal state, so Flooid's validation still sees
              // nothing selected. This version tries, in order: (1) the datepicker
              // plugin's own setDate API if one is attached to the field, (2) ISO format
              // for native type="date" controls, (3) a plain native-setter assignment,
              // then verifies the field actually kept the value - if a masked-input
              // library silently rejected or reformatted it, it falls back to (4)
              // simulating real keystrokes one character at a time, which satisfies
              // libraries that only validate as the user types rather than in bulk.
              const parseDdMmYyyy = v => { const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(v || ''); return m ? { d: +m[1], mo: +m[2], y: +m[3] } : null; };
              const applyNative = (el, val) => {
                const proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
                const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (nativeSetter) nativeSetter.call(el, val); else el.value = val;
              };
              const simulateTyping = (el, val) => {
                try { applyNative(el, ''); fire(el); } catch (_) {}
                let current = '';
                for (const ch of String(val)) {
                  current += ch;
                  try {
                    el.dispatchEvent(new KeyboardEvent('keydown', { key: ch, bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keypress', { key: ch, bubbles: true }));
                    applyNative(el, current);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keyup', { key: ch, bubbles: true }));
                  } catch (_) {}
                }
                fire(el);
              };
              const setValue = (element, value) => {
                try {
                  const parts = parseDdMmYyyy(value);

                  if (parts && window.jQuery) {
                    try {
                      const $el = window.jQuery(element);
                      const hasDatepicker = (element.classList && element.classList.contains('hasDatepicker'))
                        || (typeof $el.data === 'function' && !!$el.data('datepicker'));
                      if (hasDatepicker && typeof $el.datepicker === 'function') {
                        $el.datepicker('setDate', new Date(parts.y, parts.mo - 1, parts.d));
                        fire(element);
                        if ((element.value || '').trim()) return;
                      }
                    } catch (_) {}
                  }

                  if ((element.type || '').toLowerCase() === 'date' && parts) {
                    applyNative(element, `${String(parts.y).padStart(4, '0')}-${String(parts.mo).padStart(2, '0')}-${String(parts.d).padStart(2, '0')}`);
                    fire(element);
                    return;
                  }

                  applyNative(element, value);
                  fire(element);

                  if (parts && (element.value || '').trim() !== value) {
                    simulateTyping(element, value);
                  }
                } catch (_) {
                  try { element.value = value; fire(element); } catch (_) {}
                }
              };

              const normalise = value => (value || '')
                .toLowerCase()
                .replace(/&/g, ' and ')
                .replace(/[^a-z0-9]+/g, ' ')
                .replace(/\s+/g, ' ')
                .trim();
              const radioDescriptions = input => {
                const explicit = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
                const wrapping = input.closest('label');
                const next = input.nextElementSibling;
                const prev = input.previousElementSibling;
                const directText = input.parentNode ? [...input.parentNode.childNodes]
                  .filter(n => n.nodeType === Node.TEXT_NODE)
                  .map(n => n.textContent || '').join(' ') : '';
                const values = [
                  explicit?.textContent || '', wrapping?.textContent || '',
                  next?.textContent || '', prev?.textContent || '', directText,
                  input.value || '', input.name || '', input.id || '',
                  input.getAttribute('title') || '', input.getAttribute('aria-label') || ''
                ];
                // Flooid's live criteria pages place radio controls in one cell and
                // their visible text in a neighbouring cell. Include the nearest row
                // and small structural containers as fallbacks, but score shorter /
                // directly-associated descriptions ahead of broad containers.
                const row = input.closest('tr');
                const cell = input.closest('td,th');
                const listItem = input.closest('li');
                if (cell) values.push(cell.innerText || cell.textContent || '');
                if (row) values.push(row.innerText || row.textContent || '');
                if (listItem) values.push(listItem.innerText || listItem.textContent || '');
                let parent = input.parentElement;
                for (let depth = 0; parent && depth < 3; depth++, parent = parent.parentElement) {
                  const text = parent.innerText || parent.textContent || '';
                  if (text && text.length <= 180) values.push(text);
                }
                return [...new Set(values.map(normalise).filter(Boolean))];
              };
              const chooseRadio = label => {
                const wanted = normalise(label);
                let best = null;
                let bestRank = Number.MAX_SAFE_INTEGER;
                for (const input of form.querySelectorAll('input[type=radio]')) {
                  const descriptions = radioDescriptions(input);
                  for (const desc of descriptions) {
                    let match = 999;
                    if (desc === wanted) match = 0;
                    else if (desc.startsWith(wanted + ' ') || desc.endsWith(' ' + wanted)) match = 1;
                    else if (desc.includes(wanted)) match = 2;
                    if (match === 999) continue;
                    // Prefer exact/direct short descriptions, then the smallest row/container.
                    const rank = (match * 10000) + Math.min(desc.length, 9999);
                    if (rank < bestRank) { best = input; bestRank = rank; }
                  }
                }
                if (!best) return false;
                try { best.click(); } catch (_) { best.checked = true; }
                best.checked = true;
                fire(best);
                return best.checked;
              };
              const setVisibleDateRange = () => {
                const inputs = [...form.querySelectorAll('input')].filter(el => {
                  const type = (el.type || '').toLowerCase();
                  if (['radio','checkbox','button','submit','reset','hidden'].includes(type)) return false;
                  const key = normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                  return key.includes('date') || /^\d{2}\/\d{2}\/\d{4}$/.test((el.value || '').trim());
                });
                const keyFor = el => normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                const fromControl = inputs.find(el => keyFor(el).includes('from') || keyFor(el).includes('start')) || (inputs.length >= 2 ? inputs[inputs.length - 2] : inputs[0]);
                const toControl = inputs.find(el => keyFor(el).includes('to') || keyFor(el).includes('end')) || (inputs.length >= 2 ? inputs[inputs.length - 1] : inputs[0]);
                if (fromControl) setValue(fromControl, from);
                if (toControl) setValue(toControl, to);
                return !!fromControl && !!toControl;
              };

              // Stage 6A.74. Read from the genuine saved criteria pages of all three
              // reports (a saved copy of the page, 08 and 09), which carry an identical
              // calendar component. "For Date" is a radio, id FromToPeriodType5,
              // value 5, in the group components.calendar.reportingPeriodType. Its
              // From/To boxes are Dojo dijit.form.DateTextBox widgets, and each one
              // is THREE inputs:
              //   - a readonly validation-icon input
              //   - the visible box, id "components.calendar.searchFromDate", NO name
              //   - a hidden input, name "components.calendar.searchFromDate",
              //     carrying the value that is actually posted, in ISO yyyy-MM-dd
              // The previous loop walked form.elements, skipped everything without a
              // name - which is both visible boxes - and wrote dd/MM/yyyy into the
              // hidden ISO field. Flooid then rejected it as "From date is invalid".
              // That was reproduced offline against the real saved page before this
              // was changed. Driving the widget through dijit updates the visible box
              // and the hidden field together, which is what a real selection does.
              const toIsoDate = text => {
                const parts = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((text || '').trim());
                return parts ? parts[3] + '-' + parts[2] + '-' + parts[1] : null;
              };
              const setCalendarDate = (fieldId, text) => {
                const iso = toIsoDate(text);
                if (!iso) return fieldId + ': "' + text + '" is not dd/MM/yyyy';
                const numbers = iso.split('-').map(Number);
                const visible = document.getElementById(fieldId);
                const hidden = form.querySelector('input[type=hidden][name="' + fieldId + '"]');
                if (!visible && !hidden) return fieldId + ': neither the visible box nor the hidden field is present';
                let widget = null;
                try {
                  widget = (window.dijit && typeof window.dijit.byId === 'function')
                    ? window.dijit.byId(fieldId) : null;
                } catch (_) { widget = null; }
                if (widget) {
                  const value = new Date(numbers[0], numbers[1] - 1, numbers[2]);
                  try {
                    if (typeof widget.set === 'function') widget.set('value', value);
                    else if (typeof widget.setValue === 'function') widget.setValue(value);
                  } catch (_) {}
                }
                // Whether or not dijit was reachable, make both halves agree. The
                // hidden field is the one Flooid actually reads, so it is written
                // last and is what decides success.
                if (visible && (visible.value || '').trim() !== text) {
                  try { setValue(visible, text); } catch (_) {}
                }
                if (hidden && (hidden.value || '').trim() !== iso) {
                  try { hidden.value = iso; fire(hidden); } catch (_) {}
                }
                const landed = hidden ? (hidden.value || '').trim() : '';
                if (hidden && landed !== iso)
                  return fieldId + ': hidden field still reads "' + landed + '" rather than "' + iso + '"';
                return null;
              };
              const setCalendarDateRange = (fromText, toText) => {
                const radio = form.querySelector('#FromToPeriodType5')
                  || document.getElementById('FromToPeriodType5')
                  || form.querySelector('input[type=radio][name="components.calendar.reportingPeriodType"][value="5"]');
                if (!radio) return 'the "For Date" radio (FromToPeriodType5) is not on this form';
                const problems = [
                  setCalendarDate('components.calendar.searchFromDate', fromText),
                  setCalendarDate('components.calendar.searchToDate', toText)
                ].filter(Boolean);
                if (problems.length) { setVisibleDateRange(); return problems.join(' \u00b7 '); }
                // Checked after the dates so nothing can clear it. This radio carries
                // no onclick on any of the three saved pages, so clicking it does
                // nothing beyond checking it.
                try { radio.click(); } catch (_) { radio.checked = true; }
                radio.checked = true;
                fire(radio);
                if (!radio.checked) return 'the "For Date" radio would not stay checked';
                return 'OK';
              };

              // Stage 6A.53 fix (from a real screenshot of Flooid's criteria page):
              // "Report Periods" is NOT a set of text-labelled radios. It is a radio with
              // NO adjacent text at all, paired with a dropdown whose options are presets
              // ("Today", "Yesterday", "This Week", ...). Every earlier stage searched for
              // a radio described by the words "For Date", which does not exist on this
              // page - so no period radio was ever selected. Flooid then reported the
              // (blank, unused) manual from/to date fields as invalid, which sent stages
              // 6A.50-6A.52 chasing a date-formatting problem that was never the cause.
              // This selects the period by finding the dropdown that actually offers the
              // wanted preset and activating the radio that governs it.
              const findPresetSelect = wantedPreset => {
                const wanted = normalise(wantedPreset);
                for (const select of form.querySelectorAll('select')) {
                  const option = [...select.options].find(o => normalise(o.textContent) === wanted);
                  if (option) return { select, option };
                }
                return null;
              };
              const radioGoverning = select => {
                // Walk outwards from the dropdown looking for the radio that enables it.
                // Flooid places them in the same row, so the nearest enclosing container
                // holding exactly one radio is unambiguous and is taken directly. If a
                // container holds several, the arbitrary "first" one is NOT safe - that
                // could arm the wrong period. Fall back to the radio that most closely
                // precedes the dropdown in document order, which matches how these
                // controls are laid out (radio in one cell, its dropdown in the next).
                let node = select.parentElement;
                for (let depth = 0; node && depth < 5; depth++, node = node.parentElement) {
                  const radios = [...node.querySelectorAll('input[type=radio]')];
                  if (radios.length === 1) return radios[0];
                  if (radios.length > 1) {
                    const preceding = radios.filter(r =>
                      r.compareDocumentPosition(select) & Node.DOCUMENT_POSITION_FOLLOWING);
                    return preceding.length ? preceding[preceding.length - 1] : radios[0];
                  }
                }
                return null;
              };
              const choosePeriodPreset = wantedPreset => {
                const found = findPresetSelect(wantedPreset);
                if (!found) return false;
                const radio = radioGoverning(found.select);
                if (radio) {
                  try { radio.click(); } catch (_) { radio.checked = true; }
                  radio.checked = true;
                  fire(radio);
                }
                found.select.value = found.option.value;
                fire(found.select);
                // Only report success if the period is genuinely armed - a selected
                // preset with its radio still unchecked is exactly the state that
                // produced the "date is invalid" rejection.
                return (!radio || radio.checked) && normalise(found.select.selectedOptions[0]?.textContent) === normalise(wantedPreset);
              };

              if (!chooseRadio('By Operator')) return 'ERROR: Flooid Report Type "By Operator" was not available.';
              if (!chooseRadio('Summary')) return 'ERROR: Flooid Report Output "Summary" was not available.';
              // Stage 6A.53: arm the report period the way this page is really built.
              // For the everyday "today" pull, select the "Today" preset in the Report
              // Periods dropdown and check the radio that governs it. Only when a
              // different date/range is genuinely requested do we fall back to arming
              // the manual date-range radio and typing dates into it.
              if (skipDates) {
                if (!choosePeriodPreset('Today'))
                  return 'ERROR: Flooid Report Periods "Today" preset could not be selected.';
              }
              // Stage 6A.74: the "For Date" radio is no longer chosen by fuzzy label
              // text. setCalendarDateRange finds it by its id, FromToPeriodType5,
              // which is identical on all three saved criteria pages.
              // Stage 6A.52 redesign: only write a date into Flooid at all when a specific
              // non-default date/range was actually requested. For "today" (the everyday
              // automatic pull), Flooid's own pre-populated default is left completely
              // untouched, sidestepping the "date is invalid" rejection entirely.
              if (!skipDates) {
                const dateResult = setCalendarDateRange(from, to);
                if (dateResult !== 'OK')
                  return 'ERROR: Flooid date range could not be set \u00b7 ' + dateResult;

                for (const element of form.elements) {
                  const lower = (element.name || '').toLowerCase();
                  if (lower.includes('selectedyear')) setValue(element, year);
                }
              }

              for (const select of form.querySelectorAll('select')) {
                if (!select.name) continue;
                const context = contextFor(select);
                const options = [...select.options];
                const choose = predicate => options.find(predicate);

                if (context.includes('operator') && !context.includes('group by') && !context.includes('grouping') && !context.includes('report type')) {
                  const all = choose(o => (o.value || '') === '') || choose(o => (o.textContent || '').trim().toLowerCase() === 'all');
                  if (all) { select.value = all.value; fire(select); }
                }

                if (context.includes('group') || context.includes('report type') || context.includes('view by') || context.includes('report by')) {
                  const byOperator = choose(o => (o.textContent || '').toLowerCase().includes('operator'));
                  if (byOperator) { select.value = byOperator.value; fire(select); }
                }

                if (context.includes('summary') || context.includes('detail') || context.includes('report type')) {
                  const summary = choose(o => (o.textContent || '').trim().toLowerCase().includes('summary'));
                  if (summary) { select.value = summary.value; fire(select); }
                }
              }

              for (const input of form.querySelectorAll('input[type=radio],input[type=checkbox]')) {
                if (!input.name) continue;
                const label = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
                const labelText = ((label?.textContent || '') + ' ' + (input.parentElement?.innerText || '')).toLowerCase();
                if (labelText.includes('by operator') || labelText.trim() === 'operator' || labelText.includes('summary')) {
                  input.checked = true;
                  fire(input);
                }
              }

              const action = (form.getAttribute('action') || '').trim();
              if (!action || !action.toLowerCase().includes('submit'))
                return 'ERROR: Flooid did not provide a usable submit action on this report criteria form.';

              const controls = [...form.querySelectorAll('button[type=submit],input[type=submit],button:not([type]),button[type=button],input[type=button]')];
              // Stage 6A.53 fix: Flooid's criteria page advances with a "Next" button
              // (confirmed from a screenshot of the live page), which none of the
              // previously preferred labels matched. Worse, the old fallback took the
              // first enabled control in DOM order, which can be "Cancel" - that would
              // silently abandon the report instead of running it. "Next" is now
              // recognised explicitly, and destructive/neutral controls are never
              // chosen as the submitter under any circumstances.
              const labelOf = el => ((el.textContent || el.value || '') + ' ' + (el.getAttribute('title') || '')).toLowerCase();
              // Stage 6A.63: lookup controls open a popup search window. They are never the
              // submitter, and clicking one hijacks the report wait onto that popup.
              const isNegative = el => {
                const label = labelOf(el);
                return label.includes('cancel') || label.includes('close') || label.includes('back')
                    || label.includes('reset') || label.includes('clear') || label.includes('exit')
                    || label.includes('search') || label.includes('select') || label.includes('lookup')
                    || label.includes('find') || label.includes('browse');
              };
              const usable = controls.filter(el => !el.disabled && !isNegative(el));
              const preferred = usable.find(el => {
                const label = labelOf(el);
                return label.includes('report') || label.includes('run') || label.includes('view')
                    || label.includes('generate') || label.includes('submit') || label.includes('next')
                    || label.includes('continue') || label.includes('ok') || label.includes('finish');
              });
              // Stage 6A.63: never fall back to "the first usable control". On the real
              // Item Sales page that was the Product Search button. Submitting the form
              // directly is always safer than clicking an unidentified button.
              const submitter = preferred || null;
              setTimeout(() => {
                try {
                  if (submitter) submitter.click();
                  else if (typeof form.requestSubmit === 'function') form.requestSubmit();
                  else HTMLFormElement.prototype.submit.call(form);
                } catch (_) {
                  HTMLFormElement.prototype.submit.call(form);
                }
              }, 0);
              return 'OK';
            })()
            """
            .Replace("__FROM__", JsonSerializer.Serialize(from), StringComparison.Ordinal)
            .Replace("__TO__", JsonSerializer.Serialize(to), StringComparison.Ordinal)
            .Replace("__YEAR__", JsonSerializer.Serialize(year), StringComparison.Ordinal)
            .Replace("__SKIP_DATES__", skipDateEntry ? "true" : "false", StringComparison.Ordinal);

        timing.Mark("criteria configured");
        await SubmitCurrentCriteriaAsync(configure, "Refunds/Voids", TimeSpan.FromSeconds(15));
        await CheckForImmediateCriteriaValidationErrorAsync("Refunds/Voids");
        timing.Mark("criteria submitted");

        var reportHtml = await WaitForGeneratedReportHtmlAsync(
            new[] { "refunds", "voids", "operator" },
            "Refunds, Voids & No Sales — By Operator / Summary",
            TimeSpan.FromSeconds(35));
        timing.Mark("report generated and captured");
        return reportHtml;
    }

    // Canonical Flooid Branch criteria submit: /backoffice/branchPerformanceReportCriteriaSubmit.action
    public async Task<string> FetchBranchPerformanceHtmlAsync(DateTime fromDate, DateTime toDate)
    {
        var timing = new PhaseLog("Branch Performance");
        var succeeded = false;
        try
        {
            var html = await FetchBranchPerformanceHtmlCoreAsync(fromDate, toDate, timing);
            succeeded = true;
            return html;
        }
        finally
        {
            timing.Write(succeeded ? "ok" : "failed");
        }
    }

    private async Task<string> FetchBranchPerformanceHtmlCoreAsync(DateTime fromDate, DateTime toDate, PhaseLog timing)
    {
        await InitialiseBrowserAsync();
        EnsureReadyForAutomation();
        ResetReportPopupContext();
        ResetGeneratedReportCapture();
        timing.Mark("browser ready");

        var from = fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var to = toDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var year = toDate.Year.ToString(CultureInfo.InvariantCulture);

        // Stage 6A.52 redesign: for the everyday case - "today's" report, which is what
        // an automatic daily P&L pull actually asks for - Flooid's own pre-populated
        // default date is used as-is and nothing is written into its date fields at all.
        // This sidesteps the repeated "date is invalid" rejections entirely, since the
        // automation no longer touches the field Flooid is validating. The real date
        // Flooid used is read back out of the generated report afterwards (Metadata.
        // FromDate/ToDate), so the app always reflects what Flooid actually produced
        // rather than what was requested. A genuinely different date or range (a
        // historical day, or a weekly pull) has no way to be expressed to Flooid except
        // by writing it, so that path keeps using the hardened Stage 6A.49-6A.51 logic.
        var skipDateEntry = fromDate.Date == DateTime.Today && toDate.Date == DateTime.Today;

        await OpenReportCriteriaFromMenuAsync(
            new[] { "Branch Performance", "Branch Performance Report" },
            "Branch Performance",
            """
            (() => [...document.forms].some(
              f => (f.getAttribute('action') || '').toLowerCase().includes('branchperformancereportcriteriasubmit.action')
            ) ? 'MATCH' : 'NO')()
            """,
            new[] { "Daily Reports", "Reports" });
        timing.Mark("criteria page opened");

        var configure = """
            (() => {
              const from = __FROM__;
              const to = __TO__;
              const year = __YEAR__;
              const skipDates = __SKIP_DATES__;
              const forms = [...document.forms];
              const form = forms.find(f => (f.getAttribute('action') || '').toLowerCase().includes('branchperformancereportcriteriasubmit.action'))
                        || forms.find(f => (f.getAttribute('action') || '').toLowerCase().includes('submit'));
              if (!form) return 'ERROR: Branch Performance submit form was not found.';

              const fire = element => {
                try { element.dispatchEvent(new Event('input', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('change', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('blur', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('keyup', {bubbles:true})); } catch (_) {}
              };
              // Stage 6A.51 fix: 6A.50 handled native <input type="date"> and simple
              // controlled-input widgets, but Flooid still rejected the date range with
              // "is invalid, a valid value is required" after that fix. Flooid's
              // .action-suffixed URLs point to an older Java back office, where date
              // fields are very often driven by a jQuery UI-style datepicker plugin that
              // keeps its OWN internal "selected date" object separate from the visible
              // text box - setting .value (even correctly, even via the native setter)
              // never touches that internal state, so Flooid's validation still sees
              // nothing selected. This version tries, in order: (1) the datepicker
              // plugin's own setDate API if one is attached to the field, (2) ISO format
              // for native type="date" controls, (3) a plain native-setter assignment,
              // then verifies the field actually kept the value - if a masked-input
              // library silently rejected or reformatted it, it falls back to (4)
              // simulating real keystrokes one character at a time, which satisfies
              // libraries that only validate as the user types rather than in bulk.
              const parseDdMmYyyy = v => { const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(v || ''); return m ? { d: +m[1], mo: +m[2], y: +m[3] } : null; };
              const applyNative = (el, val) => {
                const proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
                const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (nativeSetter) nativeSetter.call(el, val); else el.value = val;
              };
              const simulateTyping = (el, val) => {
                try { applyNative(el, ''); fire(el); } catch (_) {}
                let current = '';
                for (const ch of String(val)) {
                  current += ch;
                  try {
                    el.dispatchEvent(new KeyboardEvent('keydown', { key: ch, bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keypress', { key: ch, bubbles: true }));
                    applyNative(el, current);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keyup', { key: ch, bubbles: true }));
                  } catch (_) {}
                }
                fire(el);
              };
              const setValue = (element, value) => {
                try {
                  const parts = parseDdMmYyyy(value);

                  if (parts && window.jQuery) {
                    try {
                      const $el = window.jQuery(element);
                      const hasDatepicker = (element.classList && element.classList.contains('hasDatepicker'))
                        || (typeof $el.data === 'function' && !!$el.data('datepicker'));
                      if (hasDatepicker && typeof $el.datepicker === 'function') {
                        $el.datepicker('setDate', new Date(parts.y, parts.mo - 1, parts.d));
                        fire(element);
                        if ((element.value || '').trim()) return;
                      }
                    } catch (_) {}
                  }

                  if ((element.type || '').toLowerCase() === 'date' && parts) {
                    applyNative(element, `${String(parts.y).padStart(4, '0')}-${String(parts.mo).padStart(2, '0')}-${String(parts.d).padStart(2, '0')}`);
                    fire(element);
                    return;
                  }

                  applyNative(element, value);
                  fire(element);

                  if (parts && (element.value || '').trim() !== value) {
                    simulateTyping(element, value);
                  }
                } catch (_) {
                  try { element.value = value; fire(element); } catch (_) {}
                }
              };

              const normalise = value => (value || '')
                .toLowerCase()
                .replace(/&/g, ' and ')
                .replace(/[^a-z0-9]+/g, ' ')
                .replace(/\s+/g, ' ')
                .trim();
              const radioDescriptions = input => {
                const explicit = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
                const wrapping = input.closest('label');
                const next = input.nextElementSibling;
                const prev = input.previousElementSibling;
                const directText = input.parentNode ? [...input.parentNode.childNodes]
                  .filter(n => n.nodeType === Node.TEXT_NODE)
                  .map(n => n.textContent || '').join(' ') : '';
                const values = [
                  explicit?.textContent || '', wrapping?.textContent || '',
                  next?.textContent || '', prev?.textContent || '', directText,
                  input.value || '', input.name || '', input.id || '',
                  input.getAttribute('title') || '', input.getAttribute('aria-label') || ''
                ];
                // Flooid's live criteria pages place radio controls in one cell and
                // their visible text in a neighbouring cell. Include the nearest row
                // and small structural containers as fallbacks, but score shorter /
                // directly-associated descriptions ahead of broad containers.
                const row = input.closest('tr');
                const cell = input.closest('td,th');
                const listItem = input.closest('li');
                if (cell) values.push(cell.innerText || cell.textContent || '');
                if (row) values.push(row.innerText || row.textContent || '');
                if (listItem) values.push(listItem.innerText || listItem.textContent || '');
                let parent = input.parentElement;
                for (let depth = 0; parent && depth < 3; depth++, parent = parent.parentElement) {
                  const text = parent.innerText || parent.textContent || '';
                  if (text && text.length <= 180) values.push(text);
                }
                return [...new Set(values.map(normalise).filter(Boolean))];
              };
              const chooseRadio = label => {
                const wanted = normalise(label);
                let best = null;
                let bestRank = Number.MAX_SAFE_INTEGER;
                for (const input of form.querySelectorAll('input[type=radio]')) {
                  const descriptions = radioDescriptions(input);
                  for (const desc of descriptions) {
                    let match = 999;
                    if (desc === wanted) match = 0;
                    else if (desc.startsWith(wanted + ' ') || desc.endsWith(' ' + wanted)) match = 1;
                    else if (desc.includes(wanted)) match = 2;
                    if (match === 999) continue;
                    // Prefer exact/direct short descriptions, then the smallest row/container.
                    const rank = (match * 10000) + Math.min(desc.length, 9999);
                    if (rank < bestRank) { best = input; bestRank = rank; }
                  }
                }
                if (!best) return false;
                try { best.click(); } catch (_) { best.checked = true; }
                best.checked = true;
                fire(best);
                return best.checked;
              };
              const setVisibleDateRange = () => {
                const inputs = [...form.querySelectorAll('input')].filter(el => {
                  const type = (el.type || '').toLowerCase();
                  if (['radio','checkbox','button','submit','reset','hidden'].includes(type)) return false;
                  const key = normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                  return key.includes('date') || /^\d{2}\/\d{2}\/\d{4}$/.test((el.value || '').trim());
                });
                const keyFor = el => normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                const fromControl = inputs.find(el => keyFor(el).includes('from') || keyFor(el).includes('start')) || (inputs.length >= 2 ? inputs[inputs.length - 2] : inputs[0]);
                const toControl = inputs.find(el => keyFor(el).includes('to') || keyFor(el).includes('end')) || (inputs.length >= 2 ? inputs[inputs.length - 1] : inputs[0]);
                if (fromControl) setValue(fromControl, from);
                if (toControl) setValue(toControl, to);
                return !!fromControl && !!toControl;
              };

              // Stage 6A.74. Read from the genuine saved criteria pages of all three
              // reports (a saved copy of the page, 08 and 09), which carry an identical
              // calendar component. "For Date" is a radio, id FromToPeriodType5,
              // value 5, in the group components.calendar.reportingPeriodType. Its
              // From/To boxes are Dojo dijit.form.DateTextBox widgets, and each one
              // is THREE inputs:
              //   - a readonly validation-icon input
              //   - the visible box, id "components.calendar.searchFromDate", NO name
              //   - a hidden input, name "components.calendar.searchFromDate",
              //     carrying the value that is actually posted, in ISO yyyy-MM-dd
              // The previous loop walked form.elements, skipped everything without a
              // name - which is both visible boxes - and wrote dd/MM/yyyy into the
              // hidden ISO field. Flooid then rejected it as "From date is invalid".
              // That was reproduced offline against the real saved page before this
              // was changed. Driving the widget through dijit updates the visible box
              // and the hidden field together, which is what a real selection does.
              const toIsoDate = text => {
                const parts = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((text || '').trim());
                return parts ? parts[3] + '-' + parts[2] + '-' + parts[1] : null;
              };
              const setCalendarDate = (fieldId, text) => {
                const iso = toIsoDate(text);
                if (!iso) return fieldId + ': "' + text + '" is not dd/MM/yyyy';
                const numbers = iso.split('-').map(Number);
                const visible = document.getElementById(fieldId);
                const hidden = form.querySelector('input[type=hidden][name="' + fieldId + '"]');
                if (!visible && !hidden) return fieldId + ': neither the visible box nor the hidden field is present';
                let widget = null;
                try {
                  widget = (window.dijit && typeof window.dijit.byId === 'function')
                    ? window.dijit.byId(fieldId) : null;
                } catch (_) { widget = null; }
                if (widget) {
                  const value = new Date(numbers[0], numbers[1] - 1, numbers[2]);
                  try {
                    if (typeof widget.set === 'function') widget.set('value', value);
                    else if (typeof widget.setValue === 'function') widget.setValue(value);
                  } catch (_) {}
                }
                // Whether or not dijit was reachable, make both halves agree. The
                // hidden field is the one Flooid actually reads, so it is written
                // last and is what decides success.
                if (visible && (visible.value || '').trim() !== text) {
                  try { setValue(visible, text); } catch (_) {}
                }
                if (hidden && (hidden.value || '').trim() !== iso) {
                  try { hidden.value = iso; fire(hidden); } catch (_) {}
                }
                const landed = hidden ? (hidden.value || '').trim() : '';
                if (hidden && landed !== iso)
                  return fieldId + ': hidden field still reads "' + landed + '" rather than "' + iso + '"';
                return null;
              };
              const setCalendarDateRange = (fromText, toText) => {
                const radio = form.querySelector('#FromToPeriodType5')
                  || document.getElementById('FromToPeriodType5')
                  || form.querySelector('input[type=radio][name="components.calendar.reportingPeriodType"][value="5"]');
                if (!radio) return 'the "For Date" radio (FromToPeriodType5) is not on this form';
                const problems = [
                  setCalendarDate('components.calendar.searchFromDate', fromText),
                  setCalendarDate('components.calendar.searchToDate', toText)
                ].filter(Boolean);
                if (problems.length) { setVisibleDateRange(); return problems.join(' \u00b7 '); }
                // Checked after the dates so nothing can clear it. This radio carries
                // no onclick on any of the three saved pages, so clicking it does
                // nothing beyond checking it.
                try { radio.click(); } catch (_) { radio.checked = true; }
                radio.checked = true;
                fire(radio);
                if (!radio.checked) return 'the "For Date" radio would not stay checked';
                return 'OK';
              };

              // Stage 6A.53 fix (from a real screenshot of Flooid's criteria page):
              // "Report Periods" is NOT a set of text-labelled radios. It is a radio with
              // NO adjacent text at all, paired with a dropdown whose options are presets
              // ("Today", "Yesterday", "This Week", ...). Every earlier stage searched for
              // a radio described by the words "For Date", which does not exist on this
              // page - so no period radio was ever selected. Flooid then reported the
              // (blank, unused) manual from/to date fields as invalid, which sent stages
              // 6A.50-6A.52 chasing a date-formatting problem that was never the cause.
              // This selects the period by finding the dropdown that actually offers the
              // wanted preset and activating the radio that governs it.
              const findPresetSelect = wantedPreset => {
                const wanted = normalise(wantedPreset);
                for (const select of form.querySelectorAll('select')) {
                  const option = [...select.options].find(o => normalise(o.textContent) === wanted);
                  if (option) return { select, option };
                }
                return null;
              };
              const radioGoverning = select => {
                // Walk outwards from the dropdown looking for the radio that enables it.
                // Flooid places them in the same row, so the nearest enclosing container
                // holding exactly one radio is unambiguous and is taken directly. If a
                // container holds several, the arbitrary "first" one is NOT safe - that
                // could arm the wrong period. Fall back to the radio that most closely
                // precedes the dropdown in document order, which matches how these
                // controls are laid out (radio in one cell, its dropdown in the next).
                let node = select.parentElement;
                for (let depth = 0; node && depth < 5; depth++, node = node.parentElement) {
                  const radios = [...node.querySelectorAll('input[type=radio]')];
                  if (radios.length === 1) return radios[0];
                  if (radios.length > 1) {
                    const preceding = radios.filter(r =>
                      r.compareDocumentPosition(select) & Node.DOCUMENT_POSITION_FOLLOWING);
                    return preceding.length ? preceding[preceding.length - 1] : radios[0];
                  }
                }
                return null;
              };
              const choosePeriodPreset = wantedPreset => {
                const found = findPresetSelect(wantedPreset);
                if (!found) return false;
                const radio = radioGoverning(found.select);
                if (radio) {
                  try { radio.click(); } catch (_) { radio.checked = true; }
                  radio.checked = true;
                  fire(radio);
                }
                found.select.value = found.option.value;
                fire(found.select);
                // Only report success if the period is genuinely armed - a selected
                // preset with its radio still unchecked is exactly the state that
                // produced the "date is invalid" rejection.
                return (!radio || radio.checked) && normalise(found.select.selectedOptions[0]?.textContent) === normalise(wantedPreset);
              };

              if (!chooseRadio('Daily')) return 'ERROR: Flooid Branch Report Type "Daily" was not available.';
              // Stage 6A.53: arm the report period the way this page is really built.
              // For the everyday "today" pull, select the "Today" preset in the Report
              // Periods dropdown and check the radio that governs it. Only when a
              // different date/range is genuinely requested do we fall back to arming
              // the manual date-range radio and typing dates into it.
              if (skipDates) {
                if (!choosePeriodPreset('Today'))
                  return 'ERROR: Flooid Report Periods "Today" preset could not be selected.';
              }
              // Stage 6A.74: the "For Date" radio is no longer chosen by fuzzy label
              // text. setCalendarDateRange finds it by its id, FromToPeriodType5,
              // which is identical on all three saved criteria pages.
              // Stage 6A.52 redesign: only write a date into Flooid at all when a specific
              // non-default date/range was actually requested. For "today" (the everyday
              // automatic pull), Flooid's own pre-populated default is left completely
              // untouched, sidestepping the "date is invalid" rejection entirely.
              if (!skipDates) {
                const dateResult = setCalendarDateRange(from, to);
                if (dateResult !== 'OK')
                  return 'ERROR: Flooid date range could not be set \u00b7 ' + dateResult;

                for (const element of form.elements) {
                  const lower = (element.name || '').toLowerCase();
                  if (lower.includes('selectedyear')) setValue(element, year);
                }
              }

              const action = (form.getAttribute('action') || '').trim();
              if (!action.toLowerCase().includes('branchperformancereportcriteriasubmit.action'))
                return 'ERROR: Flooid Branch Performance page did not provide the expected criteria submit action.';

              const controls = [...form.querySelectorAll('button[type=submit],input[type=submit],button:not([type]),button[type=button],input[type=button]')];
              // Stage 6A.53 fix: Flooid's criteria page advances with a "Next" button
              // (confirmed from a screenshot of the live page), which none of the
              // previously preferred labels matched. Worse, the old fallback took the
              // first enabled control in DOM order, which can be "Cancel" - that would
              // silently abandon the report instead of running it. "Next" is now
              // recognised explicitly, and destructive/neutral controls are never
              // chosen as the submitter under any circumstances.
              const labelOf = el => ((el.textContent || el.value || '') + ' ' + (el.getAttribute('title') || '')).toLowerCase();
              // Stage 6A.63: lookup controls open a popup search window. They are never the
              // submitter, and clicking one hijacks the report wait onto that popup.
              const isNegative = el => {
                const label = labelOf(el);
                return label.includes('cancel') || label.includes('close') || label.includes('back')
                    || label.includes('reset') || label.includes('clear') || label.includes('exit')
                    || label.includes('search') || label.includes('select') || label.includes('lookup')
                    || label.includes('find') || label.includes('browse');
              };
              const usable = controls.filter(el => !el.disabled && !isNegative(el));
              const preferred = usable.find(el => {
                const label = labelOf(el);
                return label.includes('report') || label.includes('run') || label.includes('view')
                    || label.includes('generate') || label.includes('submit') || label.includes('next')
                    || label.includes('continue') || label.includes('ok') || label.includes('finish');
              });
              // Stage 6A.63: never fall back to "the first usable control". On the real
              // Item Sales page that was the Product Search button. Submitting the form
              // directly is always safer than clicking an unidentified button.
              const submitter = preferred || null;
              setTimeout(() => {
                try {
                  if (submitter) submitter.click();
                  else if (typeof form.requestSubmit === 'function') form.requestSubmit();
                  else HTMLFormElement.prototype.submit.call(form);
                } catch (_) {
                  HTMLFormElement.prototype.submit.call(form);
                }
              }, 0);
              return 'OK';
            })()
            """
            .Replace("__FROM__", JsonSerializer.Serialize(from), StringComparison.Ordinal)
            .Replace("__TO__", JsonSerializer.Serialize(to), StringComparison.Ordinal)
            .Replace("__YEAR__", JsonSerializer.Serialize(year), StringComparison.Ordinal)
            .Replace("__SKIP_DATES__", skipDateEntry ? "true" : "false", StringComparison.Ordinal);

        timing.Mark("criteria configured");
        await SubmitCurrentCriteriaAsync(configure, "Branch Performance", TimeSpan.FromSeconds(15));
        await CheckForImmediateCriteriaValidationErrorAsync("Branch Performance");
        timing.Mark("criteria submitted");

        var reportHtml = await WaitForGeneratedReportHtmlAsync(
            new[] { "sales by time period daily report" },
            "Sales By Time Period Daily Report",
            TimeSpan.FromSeconds(35));
        timing.Mark("report generated and captured");
        return reportHtml;
    }

    public async Task<string> FetchGiftCardHtmlAsync(DateTime fromDate, DateTime toDate)
    {
        var timing = new PhaseLog("Gift Cards");
        var succeeded = false;
        try
        {
            var html = await FetchGiftCardHtmlCoreAsync(fromDate, toDate, timing);
            succeeded = true;
            return html;
        }
        finally
        {
            timing.Write(succeeded ? "ok" : "failed");
        }
    }

    // Stage 6A.65 diagnostics, shared since 6B.55: the product group state on the criteria
    // form. Read before submit (6A.65) and, from 6B.55, as soon as the form opens.
    private const string ProductGroupProbeScript = """
        (() => {
          const el = document.getElementById('components.productGroup.productGroupMultiples')
                  || document.querySelector('[name="components.productGroup.productGroupMultiples"]');
          const out = document.getElementById('components.productGroup.productGroupMultiplesOutput');
          const form = el && el.form ? el.form : document.forms[0];
          const posted = [];
          if (form) {
            for (const e of form.elements) {
              const n = (e.name || '');
              if (!n.toLowerCase().includes('productgroup')) continue;
              posted.push(n + '=' + JSON.stringify(String(e.value || '')) + ' [' + (e.type || e.tagName) + ']');
            }
          }
          return 'hiddenField=' + (el ? JSON.stringify(String(el.value || '')) : '<field not found>')
               + ' | displayed=' + (out ? JSON.stringify((out.textContent || '').trim()) : '<span not found>')
               + ' | productGroup form elements: ' + (posted.length ? posted.join(' ; ') : '<none>');
        })()
        """;

    private string _lastProductGroupAtOpenNote = "<not read>";

    private async Task<string> FetchGiftCardHtmlCoreAsync(DateTime fromDate, DateTime toDate, PhaseLog timing)
    {
        await InitialiseBrowserAsync();
        EnsureReadyForAutomation();
        ResetReportPopupContext();
        ResetGeneratedReportCapture();
        timing.Mark("browser ready");

        var from = fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var to = toDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var year = toDate.Year.ToString(CultureInfo.InvariantCulture);

        // Stage 6A.52 redesign: for the everyday case - "today's" report, which is what
        // an automatic daily P&L pull actually asks for - Flooid's own pre-populated
        // default date is used as-is and nothing is written into its date fields at all.
        // This sidesteps the repeated "date is invalid" rejections entirely, since the
        // automation no longer touches the field Flooid is validating. The real date
        // Flooid used is read back out of the generated report afterwards (Metadata.
        // FromDate/ToDate), so the app always reflects what Flooid actually produced
        // rather than what was requested. A genuinely different date or range (a
        // historical day, or a weekly pull) has no way to be expressed to Flooid except
        // by writing it, so that path keeps using the hardened Stage 6A.49-6A.51 logic.
        var skipDateEntry = fromDate.Date == DateTime.Today && toDate.Date == DateTime.Today;

        await OpenReportCriteriaFromMenuAsync(
            new[] { "Item Sales By Operator" },
            "Item Sales By Operator",
            """
            (() => {
              const text = (document.body?.innerText || '').toLowerCase();
              const hasTitle = text.includes('item sales') && text.includes('operator');
              const hasSubmit = [...document.forms].some(f => ((f.getAttribute('action') || '') + ' ' + (f.innerText || '')).toLowerCase().includes('submit'));
              return hasTitle && hasSubmit ? 'MATCH' : 'NO';
            })()
            """,
            new[] { "Daily Reports", "Reports" });
        timing.Mark("criteria page opened");

        // Stage 6B.55: what the product group field holds before the app touches it. The
        // choice lives in Flooid's session, not the page (ARCHITECTURE), so on a second
        // Refresh it may already say Gift Cards. If it does, the five-level picker - about
        // 4 s of every Refresh - could be skipped. This only records; nothing is skipped.
        try
        {
            _lastProductGroupAtOpenNote = await ExecuteInDocumentContainingAsync(
                GetAutomationCoreWebView2(), "product group to include", ProductGroupProbeScript);
        }
        catch (Exception probeError)
        {
            _lastProductGroupAtOpenNote = "<probe failed: " + probeError.Message + ">";
        }

        var levelScript = """
            (() => {
              const docs = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try {
                  const doc = win.document;
                  docs.push(doc);
                  for (const frame of doc.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);
              for (const doc of docs) {
                const text = (doc.body?.innerText || '').toLowerCase();
                if (!(text.includes('item sales') && text.includes('operator'))) continue;
                const fire = element => {
                  try { element.dispatchEvent(new Event('input', {bubbles:true})); } catch (_) {}
                  try { element.dispatchEvent(new Event('change', {bubbles:true})); } catch (_) {}
                };
              // Stage 6A.81: count XMLHttpRequest sends and completions on this window so
              // the wait can end when the RPC is genuinely finished rather than after a
              // guessed sleep. Installed per page, because each one is a fresh document.
              const installRpcCounter = win => {
                if (!win.__cfRpc) {
                  const state = { inFlight: 0, done: 0, baseline: 0 };
                  const send = win.XMLHttpRequest.prototype.send;
                  win.XMLHttpRequest.prototype.send = function () {
                    state.inFlight++;
                    let settled = false;
                    const finish = () => { if (settled) return; settled = true; state.inFlight--; state.done++; };
                    try { this.addEventListener('loadend', finish); } catch (_) { finish(); }
                    return send.apply(this, arguments);
                  };
                  win.__cfRpc = state;
                }
                win.__cfRpc.baseline = win.__cfRpc.done;
              };
                for (const select of doc.querySelectorAll('select')) {
                  const surrounding = ((select.closest('tr')?.innerText || select.parentElement?.innerText || '') + ' ' + (select.name || '') + ' ' + (select.id || '')).toLowerCase();
                  if (!surrounding.includes('product group level')) continue;
                  const option = [...select.options].find(o => (o.value || '') === '5'
                    || (o.textContent || '').trim() === '5'
                    || (o.textContent || '').toLowerCase().includes('level 5'));
                  if (option) {
                    select.value = option.value;
                    // Stage 6A.82: arm the counter BEFORE firing, so whatever the change
                    // handler sends can be waited on instead of slept through.
                    try { installRpcCounter(doc.defaultView || window); } catch (_) {}
                    fire(select);
                    return 'OK';
                  }
                }
              }
              return 'NO_LEVEL_CONTROL';
            })()
            """;
        var levelResult = await ExecuteStringScriptAsync(levelScript);
        if (levelResult.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(levelResult[6..].Trim());
        if (!string.Equals(levelResult, "OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Product Group Level 5 is not available on the Item Sales By Operator criteria page.");
        // Stage 6A.82: was a flat Task.Delay(1200). The 6A.80 timings showed 1204ms
        // here against about 14ms of real work. Same treatment as the picker levels:
        // wait for the change handler's request to finish, capped at the old 1200ms so
        // it cannot be slower, and identical to the old behaviour if no hook or no
        // completion is seen.
        // Stage 6A.97: this cap is now scaled, on evidence. Two runs on 16/09 both
        // reported "capped at 1200 ms · PENDING: no completion yet" - the counter was
        // installed (PENDING, not NO_HOOK) and nothing ever completed, so selecting
        // level 5 issues no XHR and this is a blind sleep waiting for something that
        // does not happen. The per-level picker caps are left unscaled because those
        // genuinely settle, in about 80ms.
        var levelSettle = await WaitForRpcQuietAsync(
            GetAutomationCoreWebView2(), "item sales",
            TimeSpan.FromMilliseconds(1200 * FlooidTimings.Scale));
        timing.Mark("product group level 5 selected · " + levelSettle);

        // Stage 6A.55: pick the Gift Cards product group through Flooid's own picker
        // before configuring the rest of the form. This must happen before the configure
        // script runs, because the configure script's submit would otherwise fire while
        // no product group had been chosen.
        await SelectGiftCardProductGroupAsync(TimeSpan.FromSeconds(90), timing);

        var configure = """
            (() => {
              const from = __FROM__;
              const to = __TO__;
              const year = __YEAR__;
              const skipDates = __SKIP_DATES__;
              const text = (document.body?.innerText || '').toLowerCase();
              const forms = [...document.forms];
              const form = forms.find(f => {
                const body = (f.innerText || '').toLowerCase();
                return (body.includes('item sales') && body.includes('operator')) || text.includes('item sales by operator');
              }) || forms.find(f => (f.getAttribute('action') || '').toLowerCase().includes('submit'));
              if (!form) return 'ERROR: Item Sales By Operator submit form was not found.';

              // Stage 6A.54: resolve which labelled group a control actually belongs to.
              // On this page every group is a <fieldset> with a <legend>, and the whole
              // form sits in a single table row - so a control's innermost fieldset is
              // the only reliable delimiter. closest('tr') matched the entire form.
              const groupContextFor = element => {
                const fieldset = element.closest ? element.closest('fieldset') : null;
                if (fieldset) {
                  const legend = fieldset.querySelector('legend');
                  return ((legend ? legend.textContent : '') + ' ' + (element.name || '') + ' ' + (element.id || '')).toLowerCase();
                }
                const cell = element.closest ? element.closest('td,th') : null;
                const cellText = cell ? (cell.innerText || cell.textContent || '') : (element.parentElement ? element.parentElement.innerText || '' : '');
                return (cellText + ' ' + (element.name || '') + ' ' + (element.id || '')).toLowerCase();
              };

              const fire = element => {
                try { element.dispatchEvent(new Event('input', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('change', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('blur', {bubbles:true})); } catch (_) {}
                try { element.dispatchEvent(new Event('keyup', {bubbles:true})); } catch (_) {}
              };
              // Stage 6A.51 fix: 6A.50 handled native <input type="date"> and simple
              // controlled-input widgets, but Flooid still rejected the date range with
              // "is invalid, a valid value is required" after that fix. Flooid's
              // .action-suffixed URLs point to an older Java back office, where date
              // fields are very often driven by a jQuery UI-style datepicker plugin that
              // keeps its OWN internal "selected date" object separate from the visible
              // text box - setting .value (even correctly, even via the native setter)
              // never touches that internal state, so Flooid's validation still sees
              // nothing selected. This version tries, in order: (1) the datepicker
              // plugin's own setDate API if one is attached to the field, (2) ISO format
              // for native type="date" controls, (3) a plain native-setter assignment,
              // then verifies the field actually kept the value - if a masked-input
              // library silently rejected or reformatted it, it falls back to (4)
              // simulating real keystrokes one character at a time, which satisfies
              // libraries that only validate as the user types rather than in bulk.
              const parseDdMmYyyy = v => { const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(v || ''); return m ? { d: +m[1], mo: +m[2], y: +m[3] } : null; };
              const applyNative = (el, val) => {
                const proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
                const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (nativeSetter) nativeSetter.call(el, val); else el.value = val;
              };
              const simulateTyping = (el, val) => {
                try { applyNative(el, ''); fire(el); } catch (_) {}
                let current = '';
                for (const ch of String(val)) {
                  current += ch;
                  try {
                    el.dispatchEvent(new KeyboardEvent('keydown', { key: ch, bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keypress', { key: ch, bubbles: true }));
                    applyNative(el, current);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new KeyboardEvent('keyup', { key: ch, bubbles: true }));
                  } catch (_) {}
                }
                fire(el);
              };
              const setValue = (element, value) => {
                try {
                  const parts = parseDdMmYyyy(value);

                  if (parts && window.jQuery) {
                    try {
                      const $el = window.jQuery(element);
                      const hasDatepicker = (element.classList && element.classList.contains('hasDatepicker'))
                        || (typeof $el.data === 'function' && !!$el.data('datepicker'));
                      if (hasDatepicker && typeof $el.datepicker === 'function') {
                        $el.datepicker('setDate', new Date(parts.y, parts.mo - 1, parts.d));
                        fire(element);
                        if ((element.value || '').trim()) return;
                      }
                    } catch (_) {}
                  }

                  if ((element.type || '').toLowerCase() === 'date' && parts) {
                    applyNative(element, `${String(parts.y).padStart(4, '0')}-${String(parts.mo).padStart(2, '0')}-${String(parts.d).padStart(2, '0')}`);
                    fire(element);
                    return;
                  }

                  applyNative(element, value);
                  fire(element);

                  if (parts && (element.value || '').trim() !== value) {
                    simulateTyping(element, value);
                  }
                } catch (_) {
                  try { element.value = value; fire(element); } catch (_) {}
                }
              };

              const normalise = value => (value || '')
                .toLowerCase()
                .replace(/&/g, ' and ')
                .replace(/[^a-z0-9]+/g, ' ')
                .replace(/\s+/g, ' ')
                .trim();
              const radioDescriptions = input => {
                const explicit = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
                const wrapping = input.closest('label');
                const next = input.nextElementSibling;
                const prev = input.previousElementSibling;
                const directText = input.parentNode ? [...input.parentNode.childNodes]
                  .filter(n => n.nodeType === Node.TEXT_NODE)
                  .map(n => n.textContent || '').join(' ') : '';
                const values = [
                  explicit?.textContent || '', wrapping?.textContent || '',
                  next?.textContent || '', prev?.textContent || '', directText,
                  input.value || '', input.name || '', input.id || '',
                  input.getAttribute('title') || '', input.getAttribute('aria-label') || ''
                ];
                // Flooid's live criteria pages place radio controls in one cell and
                // their visible text in a neighbouring cell. Include the nearest row
                // and small structural containers as fallbacks, but score shorter /
                // directly-associated descriptions ahead of broad containers.
                const row = input.closest('tr');
                const cell = input.closest('td,th');
                const listItem = input.closest('li');
                if (cell) values.push(cell.innerText || cell.textContent || '');
                if (row) values.push(row.innerText || row.textContent || '');
                if (listItem) values.push(listItem.innerText || listItem.textContent || '');
                let parent = input.parentElement;
                for (let depth = 0; parent && depth < 3; depth++, parent = parent.parentElement) {
                  const text = parent.innerText || parent.textContent || '';
                  if (text && text.length <= 180) values.push(text);
                }
                return [...new Set(values.map(normalise).filter(Boolean))];
              };
              const chooseRadio = label => {
                const wanted = normalise(label);
                let best = null;
                let bestRank = Number.MAX_SAFE_INTEGER;
                for (const input of form.querySelectorAll('input[type=radio]')) {
                  const descriptions = radioDescriptions(input);
                  for (const desc of descriptions) {
                    let match = 999;
                    if (desc === wanted) match = 0;
                    else if (desc.startsWith(wanted + ' ') || desc.endsWith(' ' + wanted)) match = 1;
                    else if (desc.includes(wanted)) match = 2;
                    if (match === 999) continue;
                    // Prefer exact/direct short descriptions, then the smallest row/container.
                    const rank = (match * 10000) + Math.min(desc.length, 9999);
                    if (rank < bestRank) { best = input; bestRank = rank; }
                  }
                }
                if (!best) return false;
                try { best.click(); } catch (_) { best.checked = true; }
                best.checked = true;
                fire(best);
                return best.checked;
              };
              const setVisibleDateRange = () => {
                const inputs = [...form.querySelectorAll('input')].filter(el => {
                  const type = (el.type || '').toLowerCase();
                  if (['radio','checkbox','button','submit','reset','hidden'].includes(type)) return false;
                  const key = normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                  return key.includes('date') || /^\d{2}\/\d{2}\/\d{4}$/.test((el.value || '').trim());
                });
                const keyFor = el => normalise((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('placeholder') || ''));
                const fromControl = inputs.find(el => keyFor(el).includes('from') || keyFor(el).includes('start')) || (inputs.length >= 2 ? inputs[inputs.length - 2] : inputs[0]);
                const toControl = inputs.find(el => keyFor(el).includes('to') || keyFor(el).includes('end')) || (inputs.length >= 2 ? inputs[inputs.length - 1] : inputs[0]);
                if (fromControl) setValue(fromControl, from);
                if (toControl) setValue(toControl, to);
                return !!fromControl && !!toControl;
              };

              // Stage 6A.74. Read from the genuine saved criteria pages of all three
              // reports (a saved copy of the page, 08 and 09), which carry an identical
              // calendar component. "For Date" is a radio, id FromToPeriodType5,
              // value 5, in the group components.calendar.reportingPeriodType. Its
              // From/To boxes are Dojo dijit.form.DateTextBox widgets, and each one
              // is THREE inputs:
              //   - a readonly validation-icon input
              //   - the visible box, id "components.calendar.searchFromDate", NO name
              //   - a hidden input, name "components.calendar.searchFromDate",
              //     carrying the value that is actually posted, in ISO yyyy-MM-dd
              // The previous loop walked form.elements, skipped everything without a
              // name - which is both visible boxes - and wrote dd/MM/yyyy into the
              // hidden ISO field. Flooid then rejected it as "From date is invalid".
              // That was reproduced offline against the real saved page before this
              // was changed. Driving the widget through dijit updates the visible box
              // and the hidden field together, which is what a real selection does.
              const toIsoDate = text => {
                const parts = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((text || '').trim());
                return parts ? parts[3] + '-' + parts[2] + '-' + parts[1] : null;
              };
              const setCalendarDate = (fieldId, text) => {
                const iso = toIsoDate(text);
                if (!iso) return fieldId + ': "' + text + '" is not dd/MM/yyyy';
                const numbers = iso.split('-').map(Number);
                const visible = document.getElementById(fieldId);
                const hidden = form.querySelector('input[type=hidden][name="' + fieldId + '"]');
                if (!visible && !hidden) return fieldId + ': neither the visible box nor the hidden field is present';
                let widget = null;
                try {
                  widget = (window.dijit && typeof window.dijit.byId === 'function')
                    ? window.dijit.byId(fieldId) : null;
                } catch (_) { widget = null; }
                if (widget) {
                  const value = new Date(numbers[0], numbers[1] - 1, numbers[2]);
                  try {
                    if (typeof widget.set === 'function') widget.set('value', value);
                    else if (typeof widget.setValue === 'function') widget.setValue(value);
                  } catch (_) {}
                }
                // Whether or not dijit was reachable, make both halves agree. The
                // hidden field is the one Flooid actually reads, so it is written
                // last and is what decides success.
                if (visible && (visible.value || '').trim() !== text) {
                  try { setValue(visible, text); } catch (_) {}
                }
                if (hidden && (hidden.value || '').trim() !== iso) {
                  try { hidden.value = iso; fire(hidden); } catch (_) {}
                }
                const landed = hidden ? (hidden.value || '').trim() : '';
                if (hidden && landed !== iso)
                  return fieldId + ': hidden field still reads "' + landed + '" rather than "' + iso + '"';
                return null;
              };
              const setCalendarDateRange = (fromText, toText) => {
                const radio = form.querySelector('#FromToPeriodType5')
                  || document.getElementById('FromToPeriodType5')
                  || form.querySelector('input[type=radio][name="components.calendar.reportingPeriodType"][value="5"]');
                if (!radio) return 'the "For Date" radio (FromToPeriodType5) is not on this form';
                const problems = [
                  setCalendarDate('components.calendar.searchFromDate', fromText),
                  setCalendarDate('components.calendar.searchToDate', toText)
                ].filter(Boolean);
                if (problems.length) { setVisibleDateRange(); return problems.join(' \u00b7 '); }
                // Checked after the dates so nothing can clear it. This radio carries
                // no onclick on any of the three saved pages, so clicking it does
                // nothing beyond checking it.
                try { radio.click(); } catch (_) { radio.checked = true; }
                radio.checked = true;
                fire(radio);
                if (!radio.checked) return 'the "For Date" radio would not stay checked';
                return 'OK';
              };

              // Stage 6A.53 fix (from a real screenshot of Flooid's criteria page):
              // "Report Periods" is NOT a set of text-labelled radios. It is a radio with
              // NO adjacent text at all, paired with a dropdown whose options are presets
              // ("Today", "Yesterday", "This Week", ...). Every earlier stage searched for
              // a radio described by the words "For Date", which does not exist on this
              // page - so no period radio was ever selected. Flooid then reported the
              // (blank, unused) manual from/to date fields as invalid, which sent stages
              // 6A.50-6A.52 chasing a date-formatting problem that was never the cause.
              // This selects the period by finding the dropdown that actually offers the
              // wanted preset and activating the radio that governs it.
              const findPresetSelect = wantedPreset => {
                const wanted = normalise(wantedPreset);
                for (const select of form.querySelectorAll('select')) {
                  const option = [...select.options].find(o => normalise(o.textContent) === wanted);
                  if (option) return { select, option };
                }
                return null;
              };
              const radioGoverning = select => {
                // Walk outwards from the dropdown looking for the radio that enables it.
                // Flooid places them in the same row, so the nearest enclosing container
                // holding exactly one radio is unambiguous and is taken directly. If a
                // container holds several, the arbitrary "first" one is NOT safe - that
                // could arm the wrong period. Fall back to the radio that most closely
                // precedes the dropdown in document order, which matches how these
                // controls are laid out (radio in one cell, its dropdown in the next).
                let node = select.parentElement;
                for (let depth = 0; node && depth < 5; depth++, node = node.parentElement) {
                  const radios = [...node.querySelectorAll('input[type=radio]')];
                  if (radios.length === 1) return radios[0];
                  if (radios.length > 1) {
                    const preceding = radios.filter(r =>
                      r.compareDocumentPosition(select) & Node.DOCUMENT_POSITION_FOLLOWING);
                    return preceding.length ? preceding[preceding.length - 1] : radios[0];
                  }
                }
                return null;
              };
              const choosePeriodPreset = wantedPreset => {
                const found = findPresetSelect(wantedPreset);
                if (!found) return false;
                const radio = radioGoverning(found.select);
                if (radio) {
                  try { radio.click(); } catch (_) { radio.checked = true; }
                  radio.checked = true;
                  fire(radio);
                }
                found.select.value = found.option.value;
                fire(found.select);
                // Only report success if the period is genuinely armed - a selected
                // preset with its radio still unchecked is exactly the state that
                // produced the "date is invalid" rejection.
                return (!radio || radio.checked) && normalise(found.select.selectedOptions[0]?.textContent) === normalise(wantedPreset);
              };

              if (!chooseRadio('Report')) return 'ERROR: Flooid Display Type "Report" was not available.';
              if (!chooseRadio('Item Sales')) return 'ERROR: Flooid Report Type "Item Sales" was not available.';
              // Stage 6A.53: arm the report period the way this page is really built.
              // For the everyday "today" pull, select the "Today" preset in the Report
              // Periods dropdown and check the radio that governs it. Only when a
              // different date/range is genuinely requested do we fall back to arming
              // the manual date-range radio and typing dates into it.
              if (skipDates) {
                if (!choosePeriodPreset('Today'))
                  return 'ERROR: Flooid Report Periods "Today" preset could not be selected.';
              }
              // Stage 6A.74: the "For Date" radio is no longer chosen by fuzzy label
              // text. setCalendarDateRange finds it by its id, FromToPeriodType5,
              // which is identical on all three saved criteria pages.
              // Stage 6A.52 redesign: only write a date into Flooid at all when a specific
              // non-default date/range was actually requested. For "today" (the everyday
              // automatic pull), Flooid's own pre-populated default is left completely
              // untouched, sidestepping the "date is invalid" rejection entirely.
              if (!skipDates) {
                const dateResult = setCalendarDateRange(from, to);
                if (dateResult !== 'OK')
                  return 'ERROR: Flooid date range could not be set \u00b7 ' + dateResult;
              }
              let giftGroupSet = false;
              let levelSet = false;
              let itemDetailSet = false;

              for (const element of form.elements) {
                const name = element.name || '';
                const lower = name.toLowerCase();
                if (!name) continue;
                if (!skipDates && lower.includes('selectedyear')) setValue(element, year);

                if (lower.includes('operator') && !lower.includes('report') && element.tagName !== 'SELECT')
                  setValue(element, '');

                if (lower.includes('showitemdetail') && (element.type || '').toLowerCase() !== 'checkbox')
                  setValue(element, element.value || 'true');

                // Stage 6A.54 fix (from a screenshot of the live Item Sales criteria page):
                // this whole form is laid out as ONE table row with three columns, so
                // closest('tr') context spanned every unrelated group on the page. Every
                // field therefore "saw" the words "Product Group Levels to Display" from
                // the left-hand column and was treated as the level field - which typed
                // "5" into "Products to Include > Product" (visible in the screenshot,
                // and it also fires Flooid's item Search, hanging on a spinner), while
                // the real Product Group field never received 600527 at all.
                // Context is now taken from each control's OWN innermost fieldset legend,
                // which is what actually delimits these groups, falling back to its cell
                // only when there is no fieldset.
                const elementType = (element.type || '').toLowerCase();
                const dataControl = (element.tagName === 'INPUT' || element.tagName === 'TEXTAREA')
                  && !['button','submit','reset','radio','checkbox'].includes(elementType);
                const key = ((name || '') + ' ' + (element.id || '')).toLowerCase();
                const ctx = groupContextFor(element);
                const inProductsToInclude = ctx.includes('products to include');
                const inLevelGroup = ctx.includes('product group level') || key.includes('productgrouplevel');
                const inProductGroup = !inLevelGroup && !inProductsToInclude
                  && (ctx.includes('product group') || (key.includes('productgroup') && !key.includes('level')));

                // Never write into "Products to Include > Product": it is an item-code
                // lookup that triggers a blocking Search, and gift cards are selected by
                // product GROUP, not by item code.
                // Stage 6A.55: the Product Group value is NOT typed in either. It is set
                // by Flooid's own five-level picker popup, which has already run by this
                // point (SelectGiftCardProductGroupAsync). Here we only CONFIRM that a
                // gift-card selection actually landed in the form, rather than
                // overwriting Flooid's own encoding with a guessed one.
                if (dataControl && inProductGroup) {
                  const current = String(element.value || '');
                  if (current.includes('600527') || current.toLowerCase().includes('gift card'))
                    giftGroupSet = true;
                }
                // The level control on this page is a <select> (handled below). Only a
                // genuine free-text level field should ever be typed into here.
                if (dataControl && inLevelGroup && !inProductsToInclude) {
                  setValue(element, '5');
                  levelSet = true;
                }
              }

              for (const select of form.querySelectorAll('select')) {
                if (!select.name) continue;
                const options = [...select.options];
                // Stage 6A.54: same fieldset-scoped context as above - the old
                // closest('tr') context matched every group in this single-row layout.
                const surrounding = groupContextFor(select);

                if (surrounding.includes('product group level')) {
                  const level = options.find(o => (o.value || '') === '5'
                    || (o.textContent || '').trim() === '5'
                    || (o.textContent || '').toLowerCase().includes('level 5'));
                  if (level) { select.value = level.value; fire(select); levelSet = true; }
                }

                if (surrounding.includes('product group') && !surrounding.includes('level')) {
                  // If this installation exposes product group as a <select> rather than
                  // the picker popup, choosing the gift-card option here is still valid.
                  const gift = options.find(o => (o.value || '').includes('600527') || (o.textContent || '').toLowerCase().includes('gift cards'));
                  if (gift) { select.value = gift.value; fire(select); giftGroupSet = true; }
                  else if ([...select.selectedOptions].some(o => (o.value || '').includes('600527')))
                    giftGroupSet = true;
                }

                if (surrounding.includes('operator')) {
                  const blank = options.find(o => (o.value || '') === '');
                  const all = options.find(o => (o.textContent || '').trim().toLowerCase() === 'all');
                  const choice = blank || all;
                  if (choice) { select.value = choice.value; fire(select); }
                }
              }

              for (const input of form.querySelectorAll('input[type=checkbox],input[type=radio]')) {
                const label = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
                const labelText = ((label?.textContent || '') + ' ' + (input.parentElement?.innerText || '') + ' ' + (input.name || '')).toLowerCase();
                if (labelText.includes('show item detail') || (input.name || '').toLowerCase().includes('showitemdetail')) {
                  input.checked = true;
                  fire(input);
                  itemDetailSet = input.checked;
                }
              }

              // Stage 6A.61: the real field is the hidden input
              // components.productGroup.productGroupMultiples (confirmed from the saved
              // criteria page). Flooid's picker writes the chosen group into it, so a
              // non-empty value there IS the selection - do not insist on the literal
              // string 600527, whose encoding is Flooid's to decide.
              if (!giftGroupSet) {
                const multiples = document.getElementById('components.productGroup.productGroupMultiples')
                               || form.querySelector('[name="components.productGroup.productGroupMultiples"]');
                if (multiples && String(multiples.value || '').trim()) giftGroupSet = true;
              }
              if (!giftGroupSet) {
                for (const element of form.elements) {
                  const v = String(element.value || '');
                  if (v.includes('600527')) { giftGroupSet = true; break; }
                }
              }
              if (!giftGroupSet)
                return 'ERROR: Product Group 600527 (Gift Cards) is not selected on the criteria form after using Flooid\'s product group picker.';
              if (!levelSet)
                return 'ERROR: Product Group Level 5 is not available on the Item Sales By Operator criteria page.';
              if (!itemDetailSet)
                return 'ERROR: Show Item Detail could not be enabled on the Item Sales By Operator criteria page.';

              const action = (form.getAttribute('action') || '').trim();
              if (!action || !action.toLowerCase().includes('submit'))
                return 'ERROR: Flooid did not provide a usable submit action on this report criteria form.';

              const controls = [...form.querySelectorAll('button[type=submit],input[type=submit],button:not([type]),button[type=button],input[type=button]')];
              // Stage 6A.53 fix: Flooid's criteria page advances with a "Next" button
              // (confirmed from a screenshot of the live page), which none of the
              // previously preferred labels matched. Worse, the old fallback took the
              // first enabled control in DOM order, which can be "Cancel" - that would
              // silently abandon the report instead of running it. "Next" is now
              // recognised explicitly, and destructive/neutral controls are never
              // chosen as the submitter under any circumstances.
              const labelOf = el => ((el.textContent || el.value || '') + ' ' + (el.getAttribute('title') || '')).toLowerCase();
              // Stage 6A.63: lookup controls open a popup search window. They are never the
              // submitter, and clicking one hijacks the report wait onto that popup.
              const isNegative = el => {
                const label = labelOf(el);
                return label.includes('cancel') || label.includes('close') || label.includes('back')
                    || label.includes('reset') || label.includes('clear') || label.includes('exit')
                    || label.includes('search') || label.includes('select') || label.includes('lookup')
                    || label.includes('find') || label.includes('browse');
              };
              const usable = controls.filter(el => !el.disabled && !isNegative(el));
              const preferred = usable.find(el => {
                const label = labelOf(el);
                return label.includes('report') || label.includes('run') || label.includes('view')
                    || label.includes('generate') || label.includes('submit') || label.includes('next')
                    || label.includes('continue') || label.includes('ok') || label.includes('finish');
              });
              // Stage 6A.63: never fall back to "the first usable control". On the real
              // Item Sales page that was the Product Search button. Submitting the form
              // directly is always safer than clicking an unidentified button.
              const submitter = preferred || null;
              setTimeout(() => {
                try {
                  if (submitter) submitter.click();
                  else if (typeof form.requestSubmit === 'function') form.requestSubmit();
                  else HTMLFormElement.prototype.submit.call(form);
                } catch (_) {
                  HTMLFormElement.prototype.submit.call(form);
                }
              }, 0);
              return 'OK';
            })()
            """
            .Replace("__FROM__", JsonSerializer.Serialize(from), StringComparison.Ordinal)
            .Replace("__TO__", JsonSerializer.Serialize(to), StringComparison.Ordinal)
            .Replace("__YEAR__", JsonSerializer.Serialize(year), StringComparison.Ordinal)
            .Replace("__SKIP_DATES__", skipDateEntry ? "true" : "false", StringComparison.Ordinal);

        // Stage 6A.65 diagnostics: read the product group state on the criteria form
        // immediately before it is submitted. Flooid's confirmButton() posts no code at
        // all - it relies entirely on the server session - so if the report comes back as
        // "All" we need to know whether the hidden field was ever populated here.

        var productGroupState = "<probe did not run>";
        try
        {
            productGroupState = await ExecuteInDocumentContainingAsync(
                GetAutomationCoreWebView2(), "product group to include", ProductGroupProbeScript);
        }
        catch (Exception probeError)
        {
            productGroupState = "<probe failed: " + probeError.Message + ">";
        }

        WriteDiagnosticFile("Gift Cards - product group state at submit", string.Join(Environment.NewLine, new[]
        {
            "at criteria open   : " + _lastProductGroupAtOpenNote,
            "picker drill       : " + _lastProductGroupDrillNote,
            "picker verify saw  : " + _lastProductGroupVerifyNote,
            "at submit time     : " + productGroupState,
            "picker retirement  : " + _lastPickerRetirementNote
        }));

        timing.Mark("criteria configured");
        await SubmitCurrentCriteriaAsync(configure, "Item Sales By Operator", TimeSpan.FromSeconds(15));
        await CheckForImmediateCriteriaValidationErrorAsync("Item Sales By Operator");
        timing.Mark("criteria submitted");

        var reportHtml = await WaitForGeneratedReportHtmlAsync(
            new[] { "item sales by operator" },
            "Item Sales By Operator — Gift Cards",
            TimeSpan.FromSeconds(40));
        timing.Mark("report generated and captured");
        return reportHtml;
    }

    // Discounts and Price Overrides (Daily Reports), for staff discounts in the Reports
    // section. Every id below is read from genuine saves of this page: the criteria form
    // (05/10/2026) and its Detailed output (10/10/2026). It is NOT built like the other
    // three criteria pages:
    //   - there is no Report Periods dropdown, so no "Today" preset - just Date From / Date
    //     To, which Flooid pre-fills with today (the 05/10 save reads 05/10/2026 in both);
    //   - the date fields are components.dateRange.fromDate / toDate, not
    //     components.calendar.searchFromDate / searchToDate;
    //   - Next is an <input type="button"> with no onclick, wrapped in
    //     <a href="javascript:setCriteriaDescriptions()">. That function copies the chosen
    //     Discount Type and Reason text into hidden fields and calls document.form.submit().
    // The report is pulled with Discount Type and Reason both "All"; staff discount lines
    // are picked out afterwards by their Reason ("25% Staff Discount"), which avoids
    // depending on the Reason list, an AJAX fill (dropdownReasonCode.action) whose codes
    // are not in any save.
    public async Task<string> FetchDiscountsHtmlAsync(DateTime fromDate, DateTime toDate)
    {
        var timing = new PhaseLog("Discounts");
        var succeeded = false;
        try
        {
            var html = await FetchDiscountsHtmlCoreAsync(fromDate, toDate, timing);
            succeeded = true;
            return html;
        }
        finally
        {
            timing.Write(succeeded ? "ok" : "failed");
        }
    }

    private async Task<string> FetchDiscountsHtmlCoreAsync(DateTime fromDate, DateTime toDate, PhaseLog timing)
    {
        const string label = "Discounts and Price Overrides";
        await InitialiseBrowserAsync();
        EnsureReadyForAutomation();
        ResetReportPopupContext();
        ResetGeneratedReportCapture();
        timing.Mark("browser ready");

        await OpenReportCriteriaFromMenuAsync(
            new[] { "Discounts and Price Overrides Report", "Discounts and Price Overrides" },
            label,
            DiscountsCriteriaMatchScript,
            new[] { "Daily Reports", "Reports" });
        timing.Mark("criteria page opened");

        // Changing Discount Type makes Flooid reload the Reason list over AJAX, so "All" is
        // put back (if anything else is showing) and then polled for rather than set and
        // submitted in one go.
        var reasonDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!await CurrentPageMatchesAsync(DiscountsReasonAllScript))
        {
            if (DateTime.UtcNow > reasonDeadline)
                throw new InvalidOperationException($"{label}: Discount Type and Reason could not both be set to \"All\" · {await SafeCurrentPageSummaryAsync()}");
            await FlooidTimings.PollAsync(300);
        }

        var skipDateEntry = fromDate.Date == DateTime.Today && toDate.Date == DateTime.Today;
        var configure = DiscountsConfigureScript
            .Replace("__FROM__", JsonSerializer.Serialize(fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("__TO__", JsonSerializer.Serialize(toDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("__SKIP_DATES__", skipDateEntry ? "true" : "false", StringComparison.Ordinal);

        timing.Mark("criteria configured");
        await SubmitCurrentCriteriaAsync(configure, label, TimeSpan.FromSeconds(15));
        await CheckForImmediateCriteriaValidationErrorAsync(label);
        timing.Mark("criteria submitted");

        // The Detailed output appears inside <iframe id="reportFrame"> on
        // discountsAndPriceOverridesReportCriteriaSubmit.action, loaded from
        // htmlReportGenerator.action - the response the capture already listens for.
        var reportHtml = await WaitForGeneratedReportHtmlAsync(
            new[] { "discounts", "price overrides", "end of report" },
            label + " — Detailed",
            TimeSpan.FromSeconds(35));
        timing.Mark("report generated and captured");
        return reportHtml;
    }

    private const string DiscountsCriteriaMatchScript = """
        (() => {
          const form = document.querySelector('form[action*="discountsandpriceoverridesreportcriteriasubmit.action" i]');
          return form && document.getElementById('nextButton') && document.getElementById('components_reasonTypeAndCode_reasonType')
            ? 'MATCH' : 'NO';
        })()
        """;

    // MATCH once Discount Type and Reason both read "All" (value ""). Setting Discount Type
    // fires Flooid's own onchange (updateReasonSelection), which replaces the Reason list.
    private const string DiscountsReasonAllScript = """
        (() => {
          const form = document.querySelector('form[action*="discountsandpriceoverridesreportcriteriasubmit.action" i]');
          const type = document.getElementById('components_reasonTypeAndCode_reasonType');
          const code = document.getElementById('components_reasonTypeAndCode_reasonCode');
          if (!form || !type || !code) return 'NO';
          if (type.value !== '') {
            type.value = '';
            type.dispatchEvent(new Event('change'));
            return 'NO';
          }
          if (code.value !== '') {
            if (![...code.options].some(o => o.value === '')) return 'NO';
            code.value = '';
          }
          return code.value === '' ? 'MATCH' : 'NO';
        })()
        """;

    private const string DiscountsConfigureScript = """
        (() => {
          const from = __FROM__;
          const to = __TO__;
          const skipDates = __SKIP_DATES__;
          const form = document.querySelector('form[action*="discountsandpriceoverridesreportcriteriasubmit.action" i]');
          if (!form) return 'NOT_HERE';

          const problems = [];
          const textNear = el => (el.parentElement?.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
          const choose = (id, wanted) => {
            const radio = document.getElementById(id);
            if (!radio) { problems.push('"' + wanted + '" (' + id + ') is not on the page'); return; }
            if (textNear(radio) !== wanted.toLowerCase()) { problems.push(id + ' reads "' + textNear(radio) + '", not "' + wanted + '"'); return; }
            if (!radio.checked) radio.click();
            radio.checked = true;
          };
          choose('components_displayType_displayType0', 'Report');
          choose('components_reportType_reportType0', 'Detail');

          const type = document.getElementById('components_reasonTypeAndCode_reasonType');
          const code = document.getElementById('components_reasonTypeAndCode_reasonCode');
          if (!type || type.value !== '') problems.push('Discount Type is not "All"');
          if (!code || code.value !== '') problems.push('Reason is not "All"');

          const operator = document.getElementById('components.user.operatorCode');
          if (operator && operator.value) operator.value = '';

          // A Date From/To box is a dijit DateTextBox: a visible dd/MM/yyyy box (id, no
          // name) and a hidden yyyy-MM-dd input (name) that is what gets posted. Today is
          // Flooid's own default, so dates are only written for another day or a range.
          const setDate = (field, text) => {
            const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text);
            if (!m) return field + ': "' + text + '" is not dd/MM/yyyy';
            const iso = m[3] + '-' + m[2] + '-' + m[1];
            const visible = document.getElementById(field);
            const hidden = form.querySelector('input[type=hidden][name="' + field + '"]');
            if (!hidden) return field + ': the hidden date field is not on the page';
            try {
              const widget = window.dijit && window.dijit.byId ? window.dijit.byId(field) : null;
              const value = new Date(+m[3], +m[2] - 1, +m[1]);
              if (widget && typeof widget.set === 'function') widget.set('value', value);
              else if (widget && typeof widget.attr === 'function') widget.attr('value', value);
            } catch (_) {}
            if (visible && visible.value !== text) visible.value = text;
            if (hidden.value !== iso) hidden.value = iso;
            return hidden.value === iso ? null : field + ': hidden field reads "' + hidden.value + '", not "' + iso + '"';
          };
          if (!skipDates) {
            for (const problem of [setDate('components.dateRange.fromDate', from), setDate('components.dateRange.toDate', to)])
              if (problem) problems.push(problem);
          }

          const next = document.getElementById('nextButton');
          if (!next || (next.value || '').trim().toLowerCase() !== 'next') problems.push('the Next button (#nextButton) is not on the page');
          if (problems.length) return 'ERROR: ' + 'Discounts and Price Overrides criteria · ' + problems.join(' · ');

          setTimeout(() => next.click(), 0);
          return 'OK';
        })()
        """;


    private async Task EnsurePrimaryFlooidReportMenuAsync()
    {
        // Stage 6A.48: report preparation must be read-only with respect to the
        // authenticated primary WebView history. Flooid's menu commonly lives in a
        // same-origin frame under /web/spring; walking Back from that shell can reach
        // the pre-authentication sign-in page and make a valid session appear signed out.
        //
        // EnsureReadyForAutomation() has already established that this application
        // currently considers the primary Flooid session signed in. Here we only detect
        // an explicit live sign-in page. Otherwise leave the page/history untouched and
        // let the frame-aware menu click locate Daily Reports / the requested report.
        var state = await GetPrimaryFlooidPageStateAsync();
        if (!string.Equals(state, "LOGIN", StringComparison.OrdinalIgnoreCase)) return;

        var summary = await SafePrimaryPageSummaryAsync();
        Publish(new FlooidSessionSnapshot(
            FlooidConnectionState.SignedOut,
            "Sign-in required",
            "Flooid is showing its sign-in page. Sign in again before updating reports.",
            Browser.Source));
        throw new InvalidOperationException($"Flooid session is on the sign-in page. Open FLOOID LOGIN and sign in again before updating. {summary}");
    }

    private async Task<string> GetPrimaryFlooidPageStateAsync()
    {
        var core = Browser.CoreWebView2 ?? throw new InvalidOperationException("Flooid browser is not ready.");
        const string script = """
            (() => {
              const docs = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try {
                  const doc = win.document;
                  docs.push(doc);
                  for (const frame of doc.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);

              let sawLogin = false;
              for (const doc of docs) {
                const text = (doc.body?.innerText || '').toLowerCase();
                const title = (doc.title || '').toLowerCase();
                const hasMenu = text.includes('daily reports') &&
                  (text.includes('refunds, voids & no sales') ||
                   text.includes('branch performance') ||
                   text.includes('item sales by operator'));
                if (hasMenu) return 'MENU';

                const password = !!doc.querySelector('input[type=password]');
                const loginOnly = text.trim() === 'login' || title.includes('login') || title.includes('sign in');
                const signIn = text.includes('sign in') && (text.includes('password') || text.includes('username'));
                if (password || loginOnly || signIn) sawLogin = true;
              }
              return sawLogin ? 'LOGIN' : 'OTHER';
            })()
            """;
        var raw = await core.ExecuteScriptAsync(script);
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase)) return "OTHER";
        try { return JsonSerializer.Deserialize<string>(raw) ?? "OTHER"; }
        catch { return raw.Trim().Trim('"'); }
    }

    private async Task<string> SafePrimaryPageSummaryAsync()
    {
        var core = Browser.CoreWebView2 ?? throw new InvalidOperationException("Flooid browser is not ready.");
        try
        {
            var raw = await core.ExecuteScriptAsync("(document.body?.innerText || document.title || '').slice(0,240)");
            var value = string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : (JsonSerializer.Deserialize<string>(raw) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(value)) return Browser.Source?.AbsolutePath ?? "no readable page text";
            var collapsed = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return collapsed.Length <= 180 ? collapsed : collapsed[..180] + "…";
        }
        catch
        {
            return Browser.Source?.AbsolutePath ?? "unreadable Flooid page";
        }
    }


    private async Task OpenReportCriteriaFromMenuAsync(
        string[] reportLabels,
        string label,
        string matchScript,
        string[]? sectionLabels = null)
    {
        // Stage 6A.46: never force the signed-in browser to bare /backoffice/.
        // The live Flooid session/menu is under /web/spring and the previous forced
        // navigation can land on a Login page. Keep the primary authenticated page,
        // or recover its real menu through WebView history before clicking a report.
        if (await CurrentPageMatchesAsync(matchScript)) return;
        await EnsurePrimaryFlooidReportMenuAsync();
        if (await CurrentPageMatchesAsync(matchScript)) return;

        var signInsAtClick = _signInCount;   // Stage 6B.54 - taken before the click starts any navigation
        var clicked = await ClickFlooidMenuItemAsync(reportLabels);
        if (!clicked && sectionLabels is { Length: > 0 })
        {
            // Some installations hide individual reports until Daily Reports/Reports is opened.
            if (await ClickFlooidMenuItemAsync(sectionLabels))
            {
                await FlooidTimings.PauseAsync(700);
                var sectionDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < sectionDeadline && !clicked)
                {
                    if (await CurrentPageMatchesAsync(matchScript)) return;
                    clicked = await ClickFlooidMenuItemAsync(reportLabels);
                    if (!clicked) await FlooidTimings.PollAsync(300);
                }
            }
        }

        if (!clicked)
        {
            var summary = await SafeCurrentPageSummaryAsync();
            throw new InvalidOperationException($"{label} was not found as a clickable item in the authenticated Flooid menu · {summary}");
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(28);
        string lastSummary = string.Empty;

        // Stage 6B.54: after a long idle, Flooid's session has expired. The click then
        // goes redirect -> /authserver/login, the automatic sign in (6B.38) signs straight
        // back in - but Flooid lands on its menu, not on the report that was asked for, and
        // this step used to wait out its 28 s there. Diagnostic log 24/09, 18:10:23 and
        // 18:55:19: "signed in after 1684 ms", then "Refunds/Voids - timings (failed)" at
        // 28 s on /web/spring/menu, then Gift Cards and Branch fine. So: once a sign in has
        // completed since the click and the menu is showing, click the report again - once.
        var clickedAgain = false;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await CurrentPageMatchesAsync(matchScript)) return;
                lastSummary = await SafeCurrentPageSummaryAsync();

                if (!clickedAgain && _signInCount != signInsAtClick && IsCurrentlySignedIn
                    && string.Equals(Browser.Source?.AbsolutePath, "/web/spring/menu", StringComparison.OrdinalIgnoreCase))
                {
                    clickedAgain = true;
                    LogRetrievalStep($"{label}: signed back in after the session expired - clicking it again");
                    if (await ClickFlooidMenuItemAsync(reportLabels))
                        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(28);
                }

                if (lastSummary.Contains("HTTP Status 500", StringComparison.OrdinalIgnoreCase)
                    || lastSummary.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Flooid returned an HTTP 500 while opening {label} · {lastSummary}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // A real menu click may be between WebView navigations. Retry until the criteria page settles.
            }

            await FlooidTimings.PollAsync(300);
        }

        var source = GetAutomationSource()?.AbsolutePath ?? "unknown page";
        throw new InvalidOperationException($"{label} was clicked in Flooid, but its criteria form did not open · {source} · {lastSummary}");
    }

    private async Task<bool> CurrentPageMatchesAsync(string matchScript)
    {
        try
        {
            var sourceJson = JsonSerializer.Serialize(matchScript);
            var wrapper = $$"""
                (() => {
                  const source = {{sourceJson}};
                  const windows = [];
                  const seen = new Set();
                  const collect = win => {
                    if (!win || seen.has(win)) return;
                    seen.add(win);
                    try { void win.document; } catch (_) { return; }
                    windows.push(win);
                    try {
                      for (const frame of win.document.querySelectorAll('iframe,frame')) {
                        try { collect(frame.contentWindow); } catch (_) {}
                      }
                    } catch (_) {}
                  };
                  collect(window);
                  for (const win of windows) {
                    try {
                      const result = win.eval(source);
                      if (String(result || '').toUpperCase() === 'MATCH') return 'MATCH';
                    } catch (_) {}
                  }
                  return 'NO';
                })()
                """;
            var result = await ExecuteStringScriptAsync(wrapper);
            return string.Equals(result, "MATCH", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ClickFlooidMenuItemAsync(string[] labels)
    {
        var labelsJson = JsonSerializer.Serialize(labels.Select(label => label.ToLowerInvariant()).ToArray());
        var script = $$"""
            (() => {
              const labels = {{labelsJson}};
              const normalise = value => (value || '')
                .toLowerCase()
                .replace(/&/g, ' and ')
                .replace(/[^a-z0-9]+/g, ' ')
                .replace(/\s+/g, ' ')
                .trim();
              const wanted = labels.map(normalise).filter(Boolean);
              const docs = [];
              const visited = new Set();

              const collect = doc => {
                if (!doc || visited.has(doc)) return;
                visited.add(doc);
                docs.push(doc);
                for (const frame of doc.querySelectorAll('iframe,frame')) {
                  try { collect(frame.contentDocument); } catch (_) {}
                }
              };
              collect(document);

              const matches = [];
              for (const doc of docs) {
                const selector = 'a,button,input[type=button],input[type=submit],[role=button],[onclick]';
                for (const node of doc.querySelectorAll(selector)) {
                  if (node.disabled) continue;
                  const rawText = (node.innerText || node.textContent || node.value || '') + ' '
                                + (node.getAttribute('title') || '') + ' '
                                + (node.getAttribute('aria-label') || '');
                  const text = normalise(rawText);
                  if (!text) continue;

                  let score = 999;
                  for (const label of wanted) {
                    if (text === label) score = Math.min(score, 0);
                    else if (text.startsWith(label) && text.length <= label.length + 35) score = Math.min(score, 1);
                    else if (text.includes(label) && text.length <= 160) score = Math.min(score, 2);
                  }
                  if (score < 999) matches.push({node, score, text});
                }
              }

              matches.sort((a,b) => a.score - b.score || a.text.length - b.text.length);
              const chosen = matches[0];
              if (!chosen) return 'NOT_FOUND';
              try { chosen.node.scrollIntoView({block:'center', inline:'nearest'}); } catch (_) {}
              try { chosen.node.focus(); } catch (_) {}
              try { chosen.node.click(); }
              catch (e) { return 'CLICK_FAILED: ' + String(e && e.message ? e.message : e); }
              return 'CLICKED: ' + chosen.text;
            })()
            """;

        try
        {
            var result = await ExecuteStringScriptAsync(script);
            return result.StartsWith("CLICKED:", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Navigation can begin quickly enough to interrupt ExecuteScriptAsync even though the click succeeded.
            await FlooidTimings.PauseAsync(250);
            return true;
        }
    }

    private async Task<string> SafeCurrentPageSummaryAsync()
    {
        try { return await GetCurrentPageSummaryAsync(); }
        catch { return GetAutomationSource()?.AbsolutePath ?? "Flooid page changing"; }
    }

    private async Task NavigateForAutomationAsync(string url, TimeSpan timeout)
    {
        var core = GetAutomationCoreWebView2();
        var absolute = ResolveAutomationUrl(url);
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) => completion.TrySetResult(e);

        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(absolute);
            var winner = await Task.WhenAny(completion.Task, Task.Delay(timeout));
            if (winner != completion.Task)
                throw new TimeoutException($"Flooid navigation timed out · {absolute}");

            var result = await completion.Task;
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Flooid navigation failed · {result.WebErrorStatus}");

            await FlooidTimings.PauseAsync(180);
        }
        finally
        {
            core.NavigationCompleted -= Completed;
        }
    }

    private string ResolveAutomationUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.AbsoluteUri;
        var current = GetAutomationSource();
        var basis = current is { IsAbsoluteUri: true } ? current : FlooidSessionService.BackOfficeEntryUri;
        return new Uri(basis, url).AbsoluteUri;
    }

    private async Task SubmitCurrentCriteriaAsync(string configureAndSubmitScript, string label, TimeSpan timeout)
    {
        var core = GetAutomationCoreWebView2();
        var sourceJson = JsonSerializer.Serialize(configureAndSubmitScript);
        var wrapper = $$"""
            (() => {
              const source = {{sourceJson}};
              const windows = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try { void win.document; } catch (_) { return; }
                windows.push(win);
                try {
                  for (const frame of win.document.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);
              const errors = [];
              for (const win of windows) {
                try {
                  const value = String(win.eval(source) ?? '');
                  if (value.toUpperCase() === 'OK') return 'OK';
                  if (value.toUpperCase().startsWith('ERROR:')) errors.push(value);
                } catch (e) {
                  errors.push('ERROR: ' + String(e && e.message ? e.message : e));
                }
              }
              return errors.length ? errors[errors.length - 1] : 'ERROR: No usable Flooid report criteria form was found in the page or its frames.';
            })()
            """;

        string result;
        try
        {
            result = await ExecuteStringScriptAsync(wrapper);
        }
        catch
        {
            // A successful submit inside a frame can navigate that frame quickly enough to
            // interrupt the script result. Give the live report navigation time to settle;
            // WaitForGeneratedReportHtmlAsync will still verify that a real report appeared.
            await FlooidTimings.PauseAsync(500);
            return;
        }

        if (result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(result[6..].Trim());
        if (!string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} criteria could not be prepared · {result}");

        // Do not require a top-level NavigationCompleted event here. Flooid commonly submits
        // the report form inside a frame while the top WebView remains on /web/spring/redirect.
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(900, Math.Max(300, timeout.TotalMilliseconds / 10))));
    }

    /// <summary>
    /// Stage 6A.51: Flooid can reject a submitted date range instantly and client-side,
    /// simply redisplaying the same criteria page with an inline validation message
    /// ("From date is invalid, a valid value is required" / same for "To date"). Before
    /// this, that situation was indistinguishable from a slow-to-generate report - the
    /// automation would wait out the full 15-35 second timeout and fail with a generic
    /// "nothing was captured" message. This polls briefly right after submit and, if that
    /// specific rejection is detected, fails immediately with Flooid's own wording plus a
    /// dump of the actual date field(s) it was trying to fill (name, id, type, current
    /// value, whether a jQuery datepicker widget is attached) - so if the Stage 6A.51
    /// date-filling fix still isn't enough for some field, the very next test's error
    /// message says exactly what is really there instead of requiring another guess.
    /// </summary>
    private async Task CheckForImmediateCriteriaValidationErrorAsync(string label)
    {
        const string probe = """
            (() => {
              const text = (document.body?.innerText || '').toLowerCase();
              const hit = (text.includes('invalid') && text.includes('date'))
                       || text.includes('a valid value is required');
              if (!hit) return 'NO';
              const inputs = [...document.querySelectorAll('input,select')].filter(el => {
                const key = ((el.name||'') + ' ' + (el.id||'') + ' ' + (el.getAttribute('placeholder')||'')).toLowerCase();
                return key.includes('date');
              });
              const jq = !!window.jQuery;
              const details = inputs.slice(0, 6).map(el => {
                const hasDatepicker = !!(el.classList && el.classList.contains('hasDatepicker'));
                return `[${el.tagName} name="${el.name||''}" id="${el.id||''}" type="${el.type||''}" value="${el.value||''}" readOnly=${!!el.readOnly} datepicker=${hasDatepicker}]`;
              });
              return 'HIT: jQuery=' + jq + ' :: ' + (details.join(' | ') || 'no date-named fields found on page');
            })()
            """;

        // Stage 6A.53: this check previously ran a fixed six polls (3 seconds) on EVERY
        // report, including successful ones - roughly nine wasted seconds per full grab.
        // It now stops as soon as the page has clearly moved past the criteria form,
        // so the cost on the success path is typically a single poll.
        const string movedOnProbe = """
            (() => {
              const url = String(location.href || '').toLowerCase();
              if (url.includes('htmlreportgenerator.action')) return 'GONE';
              const text = (document.body?.innerText || '').toLowerCase();
              const stillCriteria = text.includes('report periods')
                                 || text.includes('report output')
                                 || text.includes('criteria');
              return stillCriteria ? 'CRITERIA' : 'GONE';
            })()
            """;

        for (var i = 0; i < 6; i++)
        {
            await FlooidTimings.PollAsync(500);
            string result;
            try { result = await ExecuteStringScriptAsync(probe); }
            catch { continue; }

            if (result.StartsWith("HIT:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{label}: Flooid rejected the submitted date range immediately (\"date is invalid\") · {result[4..].Trim()}");

            try
            {
                if (string.Equals(await ExecuteStringScriptAsync(movedOnProbe), "GONE", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch
            {
                // A navigation in flight is itself evidence the submit was accepted.
                return;
            }
        }
    }

    private async Task<string> WaitForGeneratedReportHtmlAsync(string[] expectedText, string label, TimeSpan timeout)
    {
        // Stage 6A.60 safety net: never wait for a report on a product group picker
        // window. If one is still the active target at this point it can only be a
        // leftover, and following it guarantees a timeout on a page that will never
        // become a report.
        if (_activeReportBrowser is { } leftoverTarget)
        {
            // Stage 6A.63: record the outcome of this test verbatim. The test looks for
            // "productGroupPopupSearch.action"; note that a window sitting on
            // "productGroupPopupProcessSearch.action" does NOT contain that substring.
            // Whether that distinction matters here is exactly what this run should reveal,
            // so the behaviour is deliberately left unchanged.
            var leftoverUri = string.Empty;
            try { leftoverUri = leftoverTarget.Source?.AbsoluteUri ?? "<no source>"; }
            catch (Exception ex) { leftoverUri = "<unreadable: " + ex.Message + ">"; }

            if (leftoverUri.Contains("productGroupPopupSearch.action", StringComparison.OrdinalIgnoreCase))
            {
                _activeReportBrowser = null;
                _lastLeftoverDemotionNote = "demoted leftover target: " + leftoverUri;
            }
            else
            {
                _lastLeftoverDemotionNote = "KEPT active target: " + leftoverUri
                    + " (did not match the productGroupPopupSearch.action test)";
            }
        }
        else
        {
            _lastLeftoverDemotionNote = "no active report target when the wait started";
        }

        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        DateTime? generatorSeenAt = null;
        string lastSummary = string.Empty;
        // Stage 6A.49 diagnostics: if this ever times out again, we want the error message
        // to say *why* — was the criteria form (Daily/For Date/date range) still sitting there
        // unsubmitted the whole time, or did we reach Flooid's generator and just never see a
        // usable table? These two causes need completely different fixes.
        var everSawUnsubmittedCriteriaForm = false;
        var everReachedGenerator = false;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var source = GetAutomationSource()?.AbsoluteUri ?? string.Empty;
                var text = (await GetCurrentBodyTextAsync()).ToLowerInvariant();
                lastSummary = CollapseForStatus(text);

                if (source.Contains("criteria", StringComparison.OrdinalIgnoreCase)
                    || (text.Contains("for date", StringComparison.OrdinalIgnoreCase) && text.Contains("date range", StringComparison.OrdinalIgnoreCase)))
                {
                    everSawUnsubmittedCriteriaForm = true;
                }

                // First preference: the exact HTTP body returned by Flooid's generator.
                // This still works when its client-side renderer is slow or omits the report title.
                var captured = GetCapturedGeneratedReportHtml();
                if (LooksLikeGeneratedReportHtml(captured, expectedText))
                    return captured;

                var onGenerator = source.Contains("htmlReportGenerator.action", StringComparison.OrdinalIgnoreCase);
                if (onGenerator)
                {
                    everReachedGenerator = true;
                    everSawUnsubmittedCriteriaForm = false;
                    generatorSeenAt ??= DateTime.UtcNow;
                    var rendered = await GetBestRenderedReportHtmlAsync();
                    if (LooksLikeGeneratedReportHtml(rendered, expectedText))
                        return rendered;

                    // A real Flooid-side renderer failure is now distinguished from a title/detection timeout.
                    if (DateTime.UtcNow - generatorSeenAt.Value > TimeSpan.FromSeconds(12)
                        && text.Contains("failed to render", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"{label} reached Flooid's generated report page, but Flooid reported: {CollapseForStatus(text)}");
                    }
                }

                // Retain the previous title-based path as a secondary signal for installations
                // where the report renders in the current document before the URL changes.
                // Stage 6A.49 fix: the URL-based "looksLikeCriteria" guard alone was not
                // enough — an intermediate/redirect page whose URL doesn't literally contain
                // the word "criteria" but whose text still matched the keywords was previously
                // accepted as-is. The candidate document's HTML is now run through the same
                // structural check (LooksLikeGeneratedReportHtml) used everywhere else before
                // it is trusted, so an unsubmitted criteria form can never be returned here.
                var looksLikeReport = expectedText.All(term => text.Contains(term.ToLowerInvariant(), StringComparison.Ordinal));
                var looksLikeCriteria = source.Contains("criteria", StringComparison.OrdinalIgnoreCase)
                                     && !onGenerator;
                if (looksLikeReport && !looksLikeCriteria)
                {
                    var candidate = await GetDocumentHtmlContainingAsync(expectedText);
                    if (LooksLikeGeneratedReportHtml(candidate, expectedText))
                        return candidate;
                }

                // Stage 6A.47: deliberately do not navigate to htmlReportGenerator.action here.
                // A real Flooid report carries server-side report context created by the criteria
                // submit/new-window workflow. Direct navigation can reach the generator without
                // that context and make Flooid return "failed to render report, see log file".
                // Wait for the preserved popup, its real generator response, or rendered DOM.

                if (source.Contains("login", StringComparison.OrdinalIgnoreCase)
                    || (text.Contains("sign in", StringComparison.OrdinalIgnoreCase) && text.Contains("password", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Flooid session returned to the sign-in page while retrieving the report.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Keep polling: Flooid can replace the report document while scripts are running.
            }

            await FlooidTimings.PollAsync(350);
        }

        var current = GetAutomationSource()?.AbsoluteUri ?? "unknown page";
        string captureUri;
        lock (_generatedReportCaptureLock) captureUri = _lastGeneratedReportResponseUri;
        var captureDetail = string.IsNullOrWhiteSpace(captureUri) ? "no generator response body was captured" : $"generator response captured from {captureUri}";
        // Stage 6A.49 diagnostics: state plainly which of the two failure modes this was.
        var diagnosis = everReachedGenerator
            ? "Flooid's report generator was reached, but no page there ever produced a recognisable results table"
            : everSawUnsubmittedCriteriaForm
                ? "the report criteria form (date/period selection) never appeared to submit — it was still on screen when this timed out"
                : "the automation never reached either the criteria form or the report generator";
        // Stage 6A.63: the status line truncates on screen, so the full picture goes to a
        // file as well. The message keeps its original wording and adds the file path.
        var diagnosticBody = string.Join(Environment.NewLine, new[]
        {
            "label                    : " + label,
            "diagnosis                : " + diagnosis,
            "page (full url)          : " + current,
            "capture                  : " + captureDetail,
            "ever reached generator   : " + everReachedGenerator,
            "ever saw criteria form   : " + everSawUnsubmittedCriteriaForm,
            "waited                   : " + (DateTime.UtcNow - started).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s",
            string.Empty,
            "--- windows the automation can see ---",
            DescribeAutomationWindows(),
            string.Empty,
            "--- last page text seen ---",
            lastSummary
        });
        var diagnosticPath = WriteDiagnosticFile(label + " - report wait timeout", diagnosticBody);
        throw new TimeoutException($"{label} was submitted, but usable generated Flooid report HTML was not detected · {diagnosis} · page {current} · diagnostic saved to {diagnosticPath} · {captureDetail} · {lastSummary}");
    }

    private async Task<string> GetCurrentBodyTextAsync()
    {
        const string script = """
            (() => {
              const parts = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try {
                  const doc = win.document;
                  const text = doc.body?.innerText || '';
                  if (text) parts.push(text);
                  for (const frame of doc.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);
              return parts.join('\n');
            })()
            """;
        return await ExecuteStringScriptAsync(script);
    }

    private async Task<string> GetDocumentHtmlContainingAsync(string[] expectedText)
    {
        var termsJson = JsonSerializer.Serialize(expectedText.Select(term => term.ToLowerInvariant()).ToArray());
        var script = $$"""
            (() => {
              const terms = {{termsJson}};
              const docs = [];
              const seen = new Set();
              const collect = win => {
                if (!win || seen.has(win)) return;
                seen.add(win);
                try {
                  const doc = win.document;
                  docs.push(doc);
                  for (const frame of doc.querySelectorAll('iframe,frame')) {
                    try { collect(frame.contentWindow); } catch (_) {}
                  }
                } catch (_) {}
              };
              collect(window);
              for (const doc of docs) {
                try {
                  const text = (doc.body?.innerText || '').toLowerCase();
                  if (terms.every(term => text.includes(term)))
                    return doc.documentElement?.outerHTML || '';
                } catch (_) {}
              }
              return document.documentElement?.outerHTML || '';
            })()
            """;
        return await ExecuteStringScriptAsync(script);
    }

    private async Task<string> GetCurrentPageSummaryAsync()
    {
        var text = await GetCurrentBodyTextAsync();
        return CollapseForStatus(text);
    }

    private static string CollapseForStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "no readable page text";
        var collapsed = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 150 ? collapsed : collapsed[..150] + "…";
    }

    private async Task<string> ExecuteStringScriptAsync(string script)
    {
        var core = GetAutomationCoreWebView2();
        return await ExecuteStringScriptOnAsync(core, script);
    }

    /// <summary>
    /// Stage 6A.58: run a script inside whichever document actually contains the given
    /// marker text, including nested frames.
    /// <para>
    /// Flooid renders its criteria forms inside a frame while the top window stays on
    /// /web/spring/redirect - every other automation path in this file already walks
    /// frames for exactly that reason. The product group picker code did not, so it ran
    /// against the empty top-level document: it found no picker control there (falling
    /// back to window.open on the TOP window, which made Flooid write its selection back
    /// to a window that has no criteria form), and its verification then correctly
    /// reported "no product-group fields found on the criteria form".
    /// </para>
    /// <para>
    /// Evaluating through the target window means <c>window</c> inside the script is that
    /// frame's window, so window.open sets the right opener and the picker's write-back
    /// lands on the document that actually holds the form.
    /// </para>
    /// </summary>
    private static async Task<string> ExecuteInDocumentContainingAsync(CoreWebView2 core, string markerText, string script)
    {
        var wrapper = "(() => {" +
            "const source = " + JsonSerializer.Serialize(script) + ";" +
            "const marker = " + JsonSerializer.Serialize(markerText.ToLowerInvariant()) + ";" +
            "const wins = []; const seen = new Set();" +
            "const collect = w => {" +
            "  if (!w || seen.has(w)) return; seen.add(w);" +
            "  try { void w.document; } catch (_) { return; }" +
            "  wins.push(w);" +
            "  try { for (const f of w.document.querySelectorAll('iframe,frame')) { try { collect(f.contentWindow); } catch (_) {} } } catch (_) {}" +
            "};" +
            "collect(window);" +
            "let fallback = 'NO_DOCUMENT';" +
            "for (const w of wins) {" +
            "  let text = '';" +
            "  try { text = ((w.document.body && w.document.body.innerText) || '').toLowerCase(); } catch (_) { continue; }" +
            "  if (marker && !text.includes(marker)) continue;" +
            "  try { return String(w.eval(source) ?? ''); }" +
            "  catch (e) { fallback = 'ERROR: ' + String(e && e.message ? e.message : e); }" +
            "}" +
            "return fallback;" +
            "})()";

        return await ExecuteStringScriptOnAsync(core, wrapper);
    }
    private static async Task<string> ExecuteStringScriptOnAsync(CoreWebView2 core, string script)
    {
        var raw = await core.ExecuteScriptAsync(script);
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        try
        {
            return JsonSerializer.Deserialize<string>(raw) ?? string.Empty;
        }
        catch
        {
            return raw.Trim().Trim('"');
        }
    }

    /// <summary>
    /// Stage 6A.60: retire the product group picker once it has served its purpose.
    /// Leaving its window alive let later automation latch onto it instead of the
    /// criteria form. Also clears the report target if it is currently pointing at a
    /// picker window, so the report wait starts from the criteria page again.
    /// </summary>
    private void RetireProductGroupPicker()
    {
        var picker = _productGroupBrowser;
        _productGroupBrowser = null;
        _expectingProductGroupPicker = false;

        // Stage 6A.63: same substring test as the report wait, same caveat - record what it
        // decided rather than assuming. Behaviour unchanged.
        var activeUri = "<none>";
        var clearedActive = false;
        if (_activeReportBrowser is { } active)
        {
            try { activeUri = active.Source?.AbsoluteUri ?? "<no source>"; }
            catch (Exception ex) { activeUri = "<unreadable: " + ex.Message + ">"; }

            if (ReferenceEquals(active, picker)
                || activeUri.Contains("productGroupPopupSearch.action", StringComparison.OrdinalIgnoreCase))
            {
                _activeReportBrowser = null;
                clearedActive = true;
            }
        }

        var pickerUri = "<no picker>";
        if (picker is not null)
        {
            try { pickerUri = picker.Source?.AbsoluteUri ?? "<no source>"; }
            catch (Exception ex) { pickerUri = "<unreadable: " + ex.Message + ">"; }
        }

        var closedHosts = 0;
        if (picker is not null)
        {
            // Close the picker window itself so it cannot be picked up later.
            for (var i = _reportPopupHosts.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_reportPopupHosts[i].Content, picker)) continue;
                try { _reportPopupHosts[i].Close(); } catch { /* window may already be gone */ }
                _reportPopupHosts.RemoveAt(i);
                closedHosts++;
            }
        }

        _lastPickerRetirementNote =
            "picker=" + pickerUri
            + "; activeReportBrowser was " + activeUri
            + (clearedActive ? " (CLEARED)" : " (kept)")
            + "; picker hosts closed=" + closedHosts.ToString(CultureInfo.InvariantCulture)
            + "; hosts remaining=" + _reportPopupHosts.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stage 6A.63, diagnostics only: describe every window the automation can currently
    /// see, which one it would follow, and what the window-tracking code last decided.
    /// </summary>
    private string DescribeAutomationWindows()
    {
        static string Describe(Microsoft.Web.WebView2.Wpf.WebView2? view)
        {
            if (view is null) return "<none>";
            try { return view.Source?.AbsoluteUri ?? "<no source>"; }
            catch (Exception ex) { return "<unreadable: " + ex.Message + ">"; }
        }

        var lines = new List<string>
        {
            "main browser             : " + Describe(Browser),
            "_activeReportBrowser     : " + Describe(_activeReportBrowser),
            "_productGroupBrowser     : " + Describe(_productGroupBrowser),
            "_expectingProductGroupPicker : " + _expectingProductGroupPicker,
            "automation target chosen : " + (GetAutomationSource()?.AbsoluteUri ?? "<null>"),
            "open popup hosts         : " + _reportPopupHosts.Count.ToString(CultureInfo.InvariantCulture)
        };

        for (var i = 0; i < _reportPopupHosts.Count; i++)
        {
            var content = _reportPopupHosts[i].Content as Microsoft.Web.WebView2.Wpf.WebView2;
            var role = ReferenceEquals(content, _activeReportBrowser) ? "ACTIVE REPORT TARGET"
                     : ReferenceEquals(content, _productGroupBrowser) ? "picker"
                     : "unclassified";
            lines.Add("  host[" + i.ToString(CultureInfo.InvariantCulture) + "] " + role + " : " + Describe(content));
        }

        lines.Add("last picker retirement   : " + _lastPickerRetirementNote);
        lines.Add("last leftover demotion   : " + _lastLeftoverDemotionNote);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Stage 6A.63, diagnostics only: append a diagnostic block to a file on the desktop,
    /// because the in-app status line truncates long messages off screen.
    /// </summary>
    // Stage 6A.81: wait for an RPC to finish, but never for longer than the fixed
    // sleep it replaces. Returns a note for the diagnostic saying which of the three
    // outcomes happened - settled early, no hook available, or genuinely still busy
    // when the cap ran out. Stage 6A.82 generalised this from the picker to any
    // document, since the criteria page needed exactly the same treatment.
    private const string PickerRpcProbe = """
        (() => {
          const state = window.__cfRpc;
          if (!state) return 'NO_HOOK';
          if (state.inFlight > 0) return 'PENDING: ' + state.inFlight + ' in flight';
          if (state.done <= state.baseline) return 'PENDING: no completion yet';
          return 'SETTLED';
        })()
        """;

    private static async Task<string> WaitForRpcQuietAsync(CoreWebView2 target, string marker, TimeSpan cap)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = "not probed";

        // Stage 6A.82: every level settled on the very first probe at 130ms, which means
        // 120ms of that was this loop not asking yet. Probe early, then back off, so a
        // fast RPC costs a fraction of what it did and a slow one is not hammered.
        var slices = new[] { 30, 40, 60, 90, 120 };
        var attempt = 0;

        while (true)
        {
            var remaining = cap - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;

            var step = TimeSpan.FromMilliseconds(slices[Math.Min(attempt, slices.Length - 1)]);
            attempt++;
            await Task.Delay(remaining < step ? remaining : step);

            try { last = await ExecuteInDocumentContainingAsync(target, marker, PickerRpcProbe); }
            catch { continue; }

            if (string.Equals(last, "SETTLED", StringComparison.OrdinalIgnoreCase))
                return "settled after " + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms";

            if (string.Equals(last, "NO_HOOK", StringComparison.OrdinalIgnoreCase))
            {
                // No signal to wait on, so behave exactly as the old code did.
                var left = cap - clock.Elapsed;
                if (left > TimeSpan.Zero) await Task.Delay(left);
                return "no hook · waited the full " + cap.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
            }
        }

        return "capped at " + cap.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms · " + last;
    }

    // Stage 6A.80: phase timing. This changes nothing about how a report is
    // retrieved - each phase simply records how long it took, and the set is
    // appended to the desktop diagnostic file when the fetch ends, on success as
    // well as on failure. The point is to find out where a slow pull actually
    // spends its time before anything is tuned, because the fixed waits in this
    // file are guesses and the adaptive polls are not.
    private sealed class PhaseLog
    {
        // Fully qualified so no using has to be added to this file.
        private readonly System.Diagnostics.Stopwatch _total = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _phase = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<string> _marks = new();
        private readonly string _label;

        public PhaseLog(string label)
        {
            _label = label;
            // Stage 6A.96: read at the start of every retrieval, so editing the file on
            // the desktop takes effect on the next pull without restarting.
            FlooidTimings.Refresh();
        }

        public void Mark(string phase)
        {
            _marks.Add(phase.PadRight(44) + _phase.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " ms");
            _phase.Restart();
        }

        public void Write(string outcome)
        {
            _marks.Add(new string('-', 56));
            _marks.Add("timing scale".PadRight(44)
                + FlooidTimings.Scale.ToString("0.##", CultureInfo.InvariantCulture).PadLeft(7)
                + "   (" + FlooidTimings.Source + ")");
            _marks.Add("TOTAL".PadRight(44) + _total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " ms");
            WriteDiagnosticFile(_label + " - timings (" + outcome + ")", string.Join(Environment.NewLine, _marks));
        }
    }

    private static string WriteDiagnosticFile(string label, string body)
    {
        try
        {
            var path = AppFiles.DiagnosticPath;   // Stage 6B.52: no longer on the desktop
            // Stage 6B.71: size is capped in AppFiles.AppendDiagnostic (2 MB), for every writer.
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var text = "=== " + stamp + " - " + label + " ===" + Environment.NewLine
                     + body + Environment.NewLine + Environment.NewLine;
            AppFiles.AppendDiagnostic(text);
            return path;
        }
        catch (Exception ex)
        {
            return "<could not write diagnostic file: " + ex.Message + ">";
        }
    }

    /// <summary>
    /// Stage 6A.61: choose the Gift Cards product group using Flooid's own popup workflow.
    /// <para>
    /// Rewritten against the real saved markup of the criteria page and the picker, which
    /// corrected several assumptions the earlier attempts were built on:
    /// </para>
    /// <list type="bullet">
    /// <item>The criteria form lives in &lt;iframe id="iframeCenter"&gt;, and the opener is
    /// an &lt;input type="button" value="Select"&gt; whose handler is
    /// productGroupSelectPopup() - not an icon. Calling that function directly is more
    /// faithful than synthesising a click, and it also raises the modal layer Flooid
    /// expects.</item>
    /// <item>Ticking a row is NOT client-side state: its onclick calls
    /// toggleCheckbox(code, checked), which posts to
    /// productGroupPopupSearchActionRPC.action. The selection is held server-side in the
    /// session, so navigating the popup between levels does NOT lose it - deep-linking
    /// straight to the Gift Cards level is therefore safe, and the saved page proves that
    /// URL renders correctly on its own.</item>
    /// <item>Confirm calls confirmButton(), which navigates the popup to
    /// productGroupPopupProcessSearch.action; that page writes the result back into the
    /// opener's hidden field and closes the popup. So the result must be read from
    /// components.productGroup.productGroupMultiples on the criteria form, and only after
    /// the popup has closed.</item>
    /// </list>
    /// </summary>
    private async Task SelectGiftCardProductGroupAsync(TimeSpan timeout, PhaseLog? timing = null)
    {
        const string criteriaMarker = "product group to include";
        var deadline = DateTime.UtcNow + timeout;

        // Open the picker through Flooid's own function, inside the criteria iframe.
        const string openPicker = """
            (() => {
              try {
                if (typeof productGroupSelectPopup === 'function') {
                  productGroupSelectPopup();
                  return 'OPENED_VIA_FLOOID';
                }
                const btn = document.querySelector('input[name="productGroupSelectButton"]');
                if (btn) { btn.click(); return 'CLICKED_SELECT'; }
                return 'NO_OPENER';
              } catch (e) { return 'OPEN_FAILED: ' + String(e && e.message ? e.message : e); }
            })()
            """;

        _productGroupBrowser = null;
        _expectingProductGroupPicker = true;
        string opened;
        try
        {
            opened = await ExecuteInDocumentContainingAsync(
                GetAutomationCoreWebView2(), criteriaMarker, openPicker);
        }
        catch
        {
            RetireProductGroupPicker();
            throw;
        }

        // Stage 6A.61: do NOT treat a reported script error as fatal. productGroupSelectPopup()
        // calls window.open FIRST and only afterwards touches showModalLayer/jQuery, so it can
        // open the picker perfectly well and still throw on a later line. The only thing that
        // actually matters is whether the picker window appeared, so wait and judge by that.
        while (_productGroupBrowser?.CoreWebView2 is null && DateTime.UtcNow < deadline)
            await FlooidTimings.PollAsync(200);

        var picker = _productGroupBrowser?.CoreWebView2;
        if (picker is null)
        {
            RetireProductGroupPicker();
            throw new TimeoutException(
                $"Gift Cards: Flooid's Product Group picker window did not open (open attempt: {opened}).");
        }

        // Stage 6A.67: walk the hierarchy the way a person does, ticking each level on the
        // way down, instead of deep-linking to the leaf.
        //
        // Everything else has been ruled out by 6A.65 and 6A.66: the tick RPC is genuinely
        // invoked (dojo is present, toggleCheckbox does not throw), Confirm succeeds, and
        // the write-back faithfully delivers whatever the session holds - which was
        // nothing, reported as the bare delimiter "##&##". The one remaining difference
        // from the manual path that is known to work is that a person passes through
        // 200005 > 300017 > 400065 > 500303, and each of those is recorded as selected on
        // the way past, before 600527 is ticked at the bottom.
        //
        // Being honest about the standing of this: it is a hypothesis, not a demonstrated
        // cause. Why Flooid's server discards a lone leaf selection cannot be established
        // from saved pages. It is the last difference left, and the sequence below is the
        // one confirmed to work by hand.
        var basis = GetAutomationSource() ?? FlooidSessionService.BackOfficeEntryUri;

        // (url suffix, code to tick at that level). Levels taken from the real saved pages
        // in saved copies of the picker pages..07.
        var drill = new (string Query, string Code)[]
        {
            ("", "200005"),
            ("parent=200005&level=1&", "300017"),
            ("parent=300017&level=2&", "400065"),
            ("parent=400065&level=3&", "500303"),
            ("parent=500303&level=4&", "600527"),
        };

        const string tickTemplate = """
            (() => {
              // Stage 6A.66: box.checked is NOT evidence that Flooid recorded the choice.
              // The browser sets it as the click's default action even when the onclick
              // handler throws, so a click-and-check reports success while the RPC never
              // fired. toggleCheckbox -> updateRowValue -> dojo.rpc.JsonService, so this is
              // worthless until dojo and updateRowValue exist on the page.
              const code = __CODE__;
              const rows = [...document.querySelectorAll('#displayTable tr')];
              const row = rows.find(r => [...r.children].some(c => (c.textContent || '').trim() === code));
              if (!row) return 'NO_ROW';
              const box = row.querySelector('input[type=checkbox]');
              if (!box) return 'NO_CHECKBOX';
              if (typeof toggleCheckbox !== 'function') return 'NOT_READY: toggleCheckbox is not defined yet';
              if (typeof updateRowValue !== 'function') return 'NOT_READY: updateRowValue is not defined yet';
              if (typeof dojo === 'undefined' || typeof dojo.require !== 'function')
                return 'NOT_READY: dojo has not finished loading';
              if (box.checked) return 'TICKED';
              // Stage 6A.81: count XMLHttpRequest sends and completions on this window so
              // the wait can end when the RPC is genuinely finished rather than after a
              // guessed sleep. Installed per page, because each one is a fresh document.
              const installRpcCounter = win => {
                if (!win.__cfRpc) {
                  const state = { inFlight: 0, done: 0, baseline: 0 };
                  const send = win.XMLHttpRequest.prototype.send;
                  win.XMLHttpRequest.prototype.send = function () {
                    state.inFlight++;
                    let settled = false;
                    const finish = () => { if (settled) return; settled = true; state.inFlight--; state.done++; };
                    try { this.addEventListener('loadend', finish); } catch (_) { finish(); }
                    return send.apply(this, arguments);
                  };
                  win.__cfRpc = state;
                }
                win.__cfRpc.baseline = win.__cfRpc.done;
              };
              installRpcCounter(window);
              // Call Flooid's own handler directly rather than clicking. toggleCheckbox
              // uses no `this` (unlike confirmButton), so a direct call is faithful AND
              // lets a failure surface as a real exception instead of being swallowed by
              // event dispatch. The C# loop retries, so NOT_READY simply means "too early".
              try {
                box.checked = true;
                toggleCheckbox(code, true);
              } catch (e) {
                box.checked = false;
                return 'RPC_FAILED: ' + String(e && e.message ? e.message : e);
              }
              return 'TICKED';
            })()
            """;

        var drillLog = new List<string>();
        for (var step = 0; step < drill.Length; step++)
        {
            var (query, code) = drill[step];
            picker.Navigate(new Uri(basis,
                "/backoffice/productGroupPopupSearch.action?" + query
                + "name=components.productGroup.productGroupMultiples&multiple=true").AbsoluteUri);

            var tick = tickTemplate.Replace("__CODE__", JsonSerializer.Serialize(code), StringComparison.Ordinal);

            var tickResult = "picker did not finish loading";
            var ticked = false;
            while (DateTime.UtcNow < deadline)
            {
                // Stage 6A.82: was 300ms. Each tick measured about 1000ms, but that is
                // the picker page loading rounded up to the next poll, so up to 300ms of
                // every level was this loop not asking yet. The probe is cheap.
                await FlooidTimings.PollAsync(120);
                try { tickResult = await ExecuteInDocumentContainingAsync(picker, "product group level", tick); }
                catch { continue; }
                // NO_ROW also covers "this navigation has not landed yet", because the
                // readiness marker is identical on every level of the picker.
                if (string.Equals(tickResult, "TICKED", StringComparison.OrdinalIgnoreCase)) { ticked = true; break; }
            }

            drillLog.Add("level " + (step + 1) + " code " + code + " -> " + tickResult);
            timing?.Mark("picker level " + (step + 1) + " ticked");

            if (!ticked)
            {
                var note = string.Join(Environment.NewLine, drillLog);
                WriteDiagnosticFile("Gift Cards - product group drill failed", note);
                RetireProductGroupPicker();
                throw new TimeoutException(
                    $"Gift Cards: could not tick product group {code} at level {step + 1} of Flooid's picker · {tickResult}");
            }

            // Stage 6A.81: the 6A.80 timings answered the question these waits posed.
            // Of a 16.1 second Gift Cards pull, 6.1 seconds was this sleep: 907, 905,
            // 909, 910 and 2501 ms, while the ticks that preceded them took about a
            // second each of genuine page loading. So the RPC completion is now waited
            // for rather than assumed - CAPPED at the old figure, which means this can
            // never be slower than what it replaces. If the hook is missing, or no
            // completion is ever seen, it waits exactly as long as it did before.
            var settleCap = TimeSpan.FromMilliseconds(step == drill.Length - 1 ? 2500 : 900);
            var settleNote = await WaitForRpcQuietAsync(picker, "product group level", settleCap);
            drillLog.Add("level " + (step + 1) + " settle -> " + settleNote);
            timing?.Mark("picker level " + (step + 1) + " RPC settle");
        }

        _lastProductGroupDrillNote = string.Join(" | ", drillLog);

        const string confirm = """
            (() => {
              // Stage 6A.62: click the real Confirm button rather than calling
              // confirmButton() directly. Flooid's function ends with
              //     location.href = href;  this.style.cursor = "progress";
              // so invoking it as a bare function leaves `this` undefined and it throws on
              // the cosmetic cursor line - AFTER the navigation has already been started.
              // Clicking the button invokes the same handler with `this` bound to the
              // input, so nothing throws. The catch below still treats a cursor error as
              // success, because by then location.href has already been assigned.
              try {
                const btn = document.querySelector('input[name="confirm"]')
                         || [...document.querySelectorAll('input[type=button]')]
                              .find(b => (b.value || '').trim().toLowerCase() === 'confirm');
                if (btn) { btn.click(); return 'CONFIRMED'; }
                if (typeof confirmButton === 'function') { confirmButton(); return 'CONFIRMED'; }
                return 'NO_CONFIRM';
              } catch (e) {
                const message = String(e && e.message ? e.message : e);
                if (/cursor|style/i.test(message)) return 'CONFIRMED';
                return 'CONFIRM_FAILED: ' + message;
              }
            })()
            """;

        string confirmResult;
        try { confirmResult = await ExecuteInDocumentContainingAsync(picker, "product group level", confirm); }
        catch { confirmResult = "CONFIRMED"; }

        if (!string.Equals(confirmResult, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            RetireProductGroupPicker();
            throw new InvalidOperationException(
                $"Gift Cards: could not confirm the product group selection · {confirmResult}");
        }

        // The process page writes the value into the criteria form's hidden field and then
        // closes the popup, so poll the criteria form rather than assuming a fixed delay.
        // Stage 6B.57: the check must name the product group, not just see "a" group. On
        // 29/09 12:28 the 6B.56 one-page ticks came back as
        //     value="200005##&##Stamps & Carrier Bags"
        // - the top of the hierarchy - and the old test (any code at all) passed it, so the
        // Gift Cards report ran across Stamps & Carrier Bags. Only the Gift Cards code
        // (the last level of the drill) counts now, and anything else stops the retrieval.
        var verify = """
            (() => {
              const leaf = __LEAF__;
              const el = document.getElementById('components.productGroup.productGroupMultiples')
                      || document.querySelector('[name="components.productGroup.productGroupMultiples"]');
              const out = document.getElementById('components.productGroup.productGroupMultiplesOutput');
              const value = el ? String(el.value || '').trim() : '';
              const shown = out ? (out.textContent || '').trim() : '';
              // Stage 6A.66: Flooid writes back "<code>##&##<description>". With nothing
              // selected server-side it writes the bare delimiter "##&##", which the old
              // truthiness test accepted - so an empty selection passed as success and the
              // report ran across every product group. Require an actual code.
              if (value.includes(leaf + '##&##'))
                return 'OK: value="' + value + '" shown="' + shown + '"';
              if (value.split('##&##').some(part => part.trim().length > 0))
                return 'WRONG_GROUP: Flooid wrote back "' + value + '", not product group ' + leaf;
              if (value)
                return 'EMPTY_SELECTION: Flooid wrote back "' + value + '", which carries no product group code';
              return 'MISSING: field ' + (el ? 'present but empty' : 'not found')
                   + ', output "' + shown + '"';
            })()
            """.Replace("__LEAF__", JsonSerializer.Serialize(drill[^1].Code), StringComparison.Ordinal);

        string landed = "criteria form could not be read";
        while (DateTime.UtcNow < deadline)
        {
            await FlooidTimings.PollAsync(400);
            try { landed = await ExecuteInDocumentContainingAsync(GetAutomationCoreWebView2(), criteriaMarker, verify); }
            catch { continue; }
            if (landed.StartsWith("WRONG_GROUP", StringComparison.Ordinal)) break;   // Stage 6B.57: final - say so now
            if (landed.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                // Stage 6A.65: this check passes on EITHER a non-empty hidden field OR the
                // displayed text matching /gift ?card/. Those are not the same thing, so
                // record exactly what it saw rather than just that it passed.
                _lastProductGroupVerifyNote = landed;
                RetireProductGroupPicker();
                return;
            }
        }

        WriteDiagnosticFile("Gift Cards - picker verify failed", string.Join(Environment.NewLine, new[]
        {
            "picker drill      : " + _lastProductGroupDrillNote,
            "verify result     : " + landed,
            "picker retirement : " + _lastPickerRetirementNote
        }));
        RetireProductGroupPicker();
        throw new InvalidOperationException(
            "Gift Cards: the product group was confirmed in Flooid's picker, but the criteria "
            + $"form never received it · {landed}");
    }


    private void EnsureReadyForAutomation()
    {
        if (Browser.CoreWebView2 is null) throw new InvalidOperationException("Flooid browser is not ready.");
        if (!IsCurrentlySignedIn) throw new InvalidOperationException("Sign in to Flooid before retrieving reports.");
    }

    private async Task<string> RunAutomationScriptAsync(string script, TimeSpan timeout)
    {
        if (Browser.CoreWebView2 is null) throw new InvalidOperationException("Flooid browser is not ready.");
        await Browser.CoreWebView2.ExecuteScriptAsync(script);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await FlooidTimings.PollAsync(250);
            var raw = await Browser.CoreWebView2.ExecuteScriptAsync("window.__cfAutomationResult ?? null");
            if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(raw)) continue;
            var json = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var message = root.TryGetProperty("error", out var error) ? error.GetString() : "Flooid report retrieval failed.";
                throw new InvalidOperationException(message);
            }
            if (!root.TryGetProperty("html", out var html)) throw new InvalidOperationException("Flooid returned no report HTML.");
            return html.GetString() ?? string.Empty;
        }
        throw new TimeoutException("Flooid report retrieval timed out.");
    }

    // ---------------------------------------------------------------- Stage 6B.50
    // Flooid's "Select Store" page, shown after the password only to accounts with more
    // than one store. Read from the saved page a saved copy of the page:
    //   <form id="storeselectionform">, a PrimeFaces DataTable whose rows are
    //   <tr data-rk="0001"><td class="storeCode">0001</td><td>Sample Store</td></tr>,
    //   Flooid's own pick marked aria-selected="true", the choice held in the hidden
    //   input "storeselectionform:userOrgUnitTable_selection", and a submit button
    //   "storeselectionform:selectstore" whose onclick posts the form by AJAX.

    public sealed record FlooidStoreChoice(string Code, string Name, bool Preselected);

    /// <summary>
    /// The stores on Flooid's "Select Store" page, or null if the browser is not on it.
    /// Polls briefly: a single-store account passes through /web/spring/login on its way
    /// to the menu, and the page may still be loading when this is asked.
    /// </summary>
    public async Task<IReadOnlyList<FlooidStoreChoice>?> ReadStoreChoicesAsync()
    {
        const string script = """
            (() => {
              const form = document.getElementById('storeselectionform');
              if (!form) return 'NO_FORM';
              const rows = form.querySelectorAll('tbody.ui-datatable-data > tr[data-rk]');
              const stores = [];
              rows.forEach(row => {
                const cells = row.querySelectorAll('td');
                stores.push({
                  code: (row.getAttribute('data-rk') || '').trim(),
                  name: cells.length > 1 ? cells[1].textContent.trim() : '',
                  selected: row.getAttribute('aria-selected') === 'true'
                });
              });
              return JSON.stringify(stores);
            })()
            """;

        var core = Browser.CoreWebView2;
        if (core is null) return null;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var here) || !FlooidSessionService.IsLoginFlowUri(here))
                return null;

            string result;
            try { result = await ExecuteStringScriptOnAsync(core, script); }
            catch { result = string.Empty; }

            if (result.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var json = JsonDocument.Parse(result);
                    var stores = new List<FlooidStoreChoice>();
                    foreach (var item in json.RootElement.EnumerateArray())
                    {
                        var code = item.GetProperty("code").GetString() ?? string.Empty;
                        if (code.Length == 0) continue;
                        stores.Add(new FlooidStoreChoice(code,
                            item.GetProperty("name").GetString() ?? string.Empty,
                            item.GetProperty("selected").GetBoolean()));
                    }
                    return stores;
                }
                catch
                {
                    return null;
                }
            }

            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(250);
        }
    }

    /// <summary>
    /// Chooses a store on Flooid's "Select Store" page. Clicks the real row, so Flooid's
    /// own table code records the choice, then clicks the real Select Store button.
    /// Returns CLICKED:row when the row click set Flooid's selection, CLICKED:row+input
    /// when the hidden selection field had to be set as well, or NO_FORM / NO_ROW /
    /// NO_BUTTON. The button click is deferred so the answer comes back before Flooid
    /// starts to navigate (the lesson of 6B.28).
    /// </summary>
    public async Task<string> SelectStoreAsync(string code)
    {
        var script = """
            (() => {
              const code = __CODE__;
              const form = document.getElementById('storeselectionform');
              if (!form) return 'NO_FORM';
              let row = null;
              form.querySelectorAll('tbody.ui-datatable-data > tr[data-rk]').forEach(r => {
                if ((r.getAttribute('data-rk') || '').trim() === code) row = r;
              });
              if (!row) return 'NO_ROW';
              const button = document.getElementById('storeselectionform:selectstore');
              if (!button) return 'NO_BUTTON';

              (row.querySelector('td') || row).click();

              let via = 'row';
              const selection = document.getElementById('storeselectionform:userOrgUnitTable_selection');
              if (selection && selection.value !== code) { selection.value = code; via = 'row+input'; }

              setTimeout(() => button.click(), 0);
              return 'CLICKED:' + via;
            })()
            """
            .Replace("__CODE__", JsonSerializer.Serialize(code));

        try
        {
            var core = Browser.CoreWebView2;
            if (core is null) return "ERROR: Flooid browser is not ready.";
            var result = await ExecuteStringScriptOnAsync(core, script);
            return string.IsNullOrWhiteSpace(result) ? "NO_RESULT" : result;
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }
    }

    /// <summary>
    /// Stage 6B.18: fills Flooid's own login form and clicks its own submit button.
    ///
    /// Read from the genuine save at a saved copy of the page. The
    /// whole page is 2.5 KB and the form is four elements:
    ///     &lt;form id="login" method="POST" action=".../authserver/login"&gt;
    ///     &lt;input name="Uname" id="Uname" maxlength="60"&gt;
    ///     &lt;input name="Pass"  id="Pass"  maxlength="1024" type="Password"&gt;
    ///     &lt;input id="basicLoginButton" type="submit"&gt;
    /// No CSRF token, no hidden fields, no viewstate - which is what makes this possible
    /// at all. Nothing is reverse-engineered: the real button is clicked so Flooid's own
    /// form does the submitting.
    ///
    /// The values go in through the native value setter and then fire input and change,
    /// the same approach the criteria automation uses, because assigning .value directly
    /// can be missed by a framework listening for events.
    /// </summary>
    public async Task<string> SubmitLoginAsync(string username, string password)
    {
        var script = """
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

              // Stage 6B.28: the click is deferred so this answer is returned BEFORE the page
              // starts to unload. Clicking synchronously begins a navigation while the script
              // result is still being marshalled back, and the result can be swallowed - the
              // app then reads an empty answer, decides the form was not found, and hands
              // over to Flooid's page even though the sign in was going through underneath.
              // That is the "it switched to Flooid" report of 22/09. The sign-out written in
              // 6B.27 already does it this way; this is the same fix applied here.
              if (button) { setTimeout(() => button.click(), 0); return 'CLICKED'; }
              setTimeout(() => form.submit(), 0);
              return 'SUBMITTED';
            })()
            """
            .Replace("__USER__", JsonSerializer.Serialize(username))
            .Replace("__PASS__", JsonSerializer.Serialize(password));

        try
        {
            // Stage 6B.26: NOT EnsureReadyForAutomation. That guard was written for report
            // retrieval and throws unless IsCurrentlySignedIn - which at the login page is
            // false by definition. So from 6B.18 onwards this threw before the script ran,
            // every time: the credentials were never once typed into Flooid. Only the
            // browser itself needs to be ready here. And Browser directly, rather than
            // GetAutomationCoreWebView2, which prefers a report popup when one is active -
            // the login form is only ever in the main browser.
            var core = Browser.CoreWebView2;
            if (core is null) return "ERROR: Flooid browser is not ready.";

            // Stage 6B.38: the automatic sign in returned NO_FORM three times out of three
            // on 23/09, in the same second it started, while every typed sign in returned
            // CLICKED. It is triggered from UpdateCustomLoginPanel, which also runs on
            // NavigationStarting - and that reports the URL being navigated TO, so the
            // script ran against the page being left, which has no login form. Wait for
            // the form rather than for a particular event. Only NO_FORM is retried: it
            // means the script ran and touched nothing, so trying again cannot submit
            // twice. The caller caps the whole submit at 15 seconds.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var result = await ExecuteStringScriptOnAsync(core, script);
            while (result == "NO_FORM" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                result = await ExecuteStringScriptOnAsync(core, script);
            }
            return string.IsNullOrWhiteSpace(result) ? "NO_RESULT" : result;
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }
    }

    /// <summary>
    /// Stage 6B.21: reads the signed-in store from the back office page.
    ///
    /// From the genuine save at a saved copy of the page:
    ///     &lt;div id="store-info" class="topBarText"&gt;
    ///         0001 - Sample Store
    ///     &lt;/div&gt;
    /// A stable id on the page the application lands on immediately after sign in, so the
    /// store is known before any report is pulled. Until now it came out of a report's
    /// Outlet field, which meant the header could not name the store until the first
    /// retrieval had finished.
    ///
    /// Returns an empty string rather than throwing when the element is absent - on the
    /// login page, mid-navigation, or if Flooid changes it. An empty store simply leaves
    /// the header showing nothing, which is what it did before.
    /// </summary>
    public async Task<string> TryReadStoreAsync()
    {
        const string script = """
            (() => {
              const element = document.getElementById('store-info');
              if (!element) return '';
              return (element.textContent || '').replace(/\s+/g, ' ').trim();
            })()
            """;

        try
        {
            if (!_initialised || Browser.CoreWebView2 is null) return string.Empty;
            var result = await ExecuteStringScriptAsync(script);
            return result ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Stage 6B.27: signs out through Flooid's own Log Out link.
    ///
    /// From the genuine save at a saved copy of the page, Log Out is a
    /// JSF postback on a generated control id:
    ///     onclick="mojarra.jsfcljs(document.getElementById('headerForm'),
    ///              {'headerForm:j_idt29':'headerForm:j_idt29'},'');return false"
    /// j_idt ids are assigned by JSF at build time and change between Flooid deployments,
    /// so the id is not targeted. The link is found by its visible text and clicked - the
    /// real control, so Flooid ends the session on its side as well as ours, which is the
    /// same "click the real control" rule the criteria automation follows.
    ///
    /// Falls back to clearing this browser's cookies if the link is not there, so a sign
    /// out always takes effect locally even when Flooid's page is not the one showing.
    /// </summary>
    public async Task<string> SignOutAsync()
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

        try
        {
            var core = Browser.CoreWebView2;
            if (core is null) return "ERROR: Flooid browser is not ready.";

            // The click is deferred with setTimeout so this answer is returned before the
            // page starts to unload - otherwise the navigation can swallow the result.
            var result = await ExecuteStringScriptOnAsync(core, script);
            if (result == "CLICKED") return result;

            core.CookieManager.DeleteAllCookies();
            core.Reload();
            return "COOKIES_CLEARED";
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }
    }

    public void Shutdown()
    {
        ResetReportPopupContext();
        ResetGeneratedReportCapture();
        try
        {
            if (Browser.CoreWebView2 is not null)
            {
                Browser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
                Browser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
                Browser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
            }
        }
        catch
        {
            // WebView2 may already be tearing down during window shutdown.
        }

        try { Browser.Dispose(); } catch { }
        _initialised = false;
        _initialising = false;
        IsCurrentlySignedIn = false;
    }

    private void ShowBrowserError(string message)
    {
        BrowserErrorText.Text = message;
        BrowserErrorPanel.Visibility = Visibility.Visible;
        Publish(new FlooidSessionSnapshot(FlooidConnectionState.Error, "Flooid unavailable", message));
    }

    // Stage 6B.54: counts every return to signed in, so a report step can tell that the
    // session expired and was signed back into while it was waiting.
    private int _signInCount;

    private static void LogRetrievalStep(string step)
    {
        try
        {
            AppFiles.AppendDiagnostic(
                "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - retrieval step: " + step + " ===\r\n");
        }
        catch { }
    }

    private void Publish(FlooidSessionSnapshot snapshot)
    {
        var wasSignedIn = IsCurrentlySignedIn;
        IsCurrentlySignedIn = snapshot.State == FlooidConnectionState.SignedIn;
        if (!wasSignedIn && IsCurrentlySignedIn) _signInCount++;
        ConnectionStatusChanged?.Invoke(this, new FlooidConnectionStatusChangedEventArgs(snapshot));
    }
}
