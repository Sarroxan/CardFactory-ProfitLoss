# Architecture & Flooid Integration

## Solution layout

```
CardFactory.ProfitLoss.sln
├── src/
│   ├── CardFactory.ProfitLoss.App           WPF app (.NET 8, Windows)
│   │   ├── MainWindow.xaml(.cs)             UI, refresh orchestration
│   │   ├── ViewModels/MainViewModel.cs      state, calculations, bindings
│   │   └── Views/FlooidView.xaml(.cs)       ★ embedded browser + all automation
│   ├── CardFactory.ProfitLoss.Core          P&L and team performance maths
│   ├── CardFactory.ProfitLoss.Flooid        session / sign-in state
│   ├── CardFactory.ProfitLoss.ReportParsing strict HTML parsers + models
│   └── CardFactory.ProfitLoss.Storage       local save/load
└── tests/                                   unit tests for Core + ReportParsing
```

`FlooidView.xaml.cs` is by far the most complex file (~2600 lines) and is
where essentially every bug in this project has lived. It hosts a WebView2
browser and drives Flooid's real web UI.

## The three reports

| Report | Flooid menu item | Parser | Used for |
|---|---|---|---|
| Refunds/Voids | Refunds, Voids & No Sales Report | `RefundsVoidsReportParser` | per-operator refunds, voids, no-sales |
| Branch Performance | Branch Performance | `BranchPerformanceReportParser` | hourly/daily sales, transactions, units |
| Gift Cards | Item Sales By Operator Report | `GiftCardReportParser` | gift card sales to exclude from ABV/AUB |

Team Performance = Refunds/Voids merged with Gift Cards
(`OperatorReportMerger`).

**Store selection.** Only where Flooid asks for it (Stage 6B.50): an account
with several stores is shown Flooid's "Select Store" page after the password,
and the app offers that list, remembering the choice. Otherwise Flooid returns data for the
signed-in account's store, and each parsed report carries its own `Outlet`
and date range (`ParsedReportMetadata`), which the app displays. No store
number exists anywhere in the source. The only hardcoded, potentially
store-varying value is the gift card product group `600527` and the
hierarchy used to reach it.

## How a report is retrieved

1. `OpenReportCriteriaFromMenuAsync` — find the menu item by fuzzy text match
   and click it.
2. Configure script — select report type/output radios, arm the report
   period, and (gift cards only) choose the product group.
3. `SubmitCurrentCriteriaAsync` — click the submit control ("Next").
4. `CheckForImmediateCriteriaValidationErrorAsync` — catch Flooid's inline
   validation errors fast instead of waiting out the timeout.
5. `WaitForGeneratedReportHtmlAsync` — poll for the generated report, either
   captured from `htmlReportGenerator.action` responses or detected in the
   DOM.
6. Parser turns HTML into a typed result; the app reads the report's own
   date range back out of it.

## Facts established from the real saved pages

These were verified against saved copies of the live pages, not assumed.

### Frames
The criteria form is inside `<iframe id="iframeCenter">`; the top-level
document holds no forms. `ExecuteInDocumentContainingAsync(core, marker,
script)` walks frames, finds the document containing the marker text, and
evaluates through *that* window — which also makes `window.open` set the
correct `opener`.

### Report Periods
Not text-labelled radios. It is an **unlabelled radio paired with a preset
dropdown** ("Today", "Yesterday", "This Week", ...), plus separate rows for
week/period/month/quarter and a manual date range. For "today" pulls the app
selects the Today preset and writes **no dates at all**, which sidesteps
Flooid's date validation entirely.

### Product Group ("Product Group to Include")
- Opener: `<input type="button" name="productGroupSelectButton" value="Select"
  onclick="productGroupSelectPopup()">`
- `productGroupSelectPopup()` opens
  `/backoffice/productGroupPopupSearch.action?multiple=true&name=components.productGroup.productGroupMultiples`
  as window **`ProductGroupSelection`**, raises Flooid's modal layer, then
  starts `iterativelyCheckIfChildWindowIsClosed`.
- Gift cards hierarchy:
  `200005 → 300017 → 400065 → 500303 → 600527 Gift Cards`
  Final level is directly addressable:
  `productGroupPopupSearch.action?parent=500303&name=components.productGroup.productGroupMultiples&level=4&multiple=true`
- Ticking a row calls `toggleCheckbox(code, checked)` → POSTs to
  `productGroupPopupSearchActionRPC.action`. **Selection is server-side**, so
  navigating between levels does not lose it.
- `confirmButton()` navigates the popup to
  `productGroupPopupProcessSearch.action?...&level=5&multiple=true`; that page
  writes the result into the opener's hidden field
  **`components.productGroup.productGroupMultiples`** (id and name both) and
  closes the popup. There is also a display span
  `components.productGroup.productGroupMultiplesOutput`.
- Rows are drillable only when the row's hidden `hiddenField` is `false`
  (`checkRowClickable`). For 600527 it is `true` — a leaf, so it is ticked,
  not clicked through.

### Submit
The criteria page advances with a button labelled **"Next"**. "Cancel"
appears before it in the DOM, so submit-control selection must exclude
cancel/close/back/reset and must not fall back to "first enabled button".

### Layout
The criteria form is a single table row with three columns, so
`closest('tr')` context spans unrelated groups. Field meaning must come from
the field's own `<fieldset>` legend. Real legends:
Display Type · Product Group Levels to Display · Item Detail to Include ·
Report Type · Products to Include · Product Group to Include ·
Report Periods · Operator to Include.

Never write into "Products to Include → Product": it is an item-code lookup
that fires a blocking Search.

## Popup handling

Flooid opens report output *and* the product group picker as popups. Popups
are captured via `NewWindowRequested` into off-screen windows. The picker is
tracked separately (`_productGroupBrowser`, plus an expectation flag) so it
never becomes the report automation target — that mix-up caused a hang.
`RetireProductGroupPicker()` closes it and clears any stale report target.

## Build & CI

- `.github/workflows/build.yml` builds on a GitHub-hosted Windows runner,
  runs the unit tests, and publishes a self-contained `win-x64` single-file
  build as a downloadable artifact.
- Requires the Microsoft Edge WebView2 Runtime on the machine that runs it.
