# Card Factory Profit & Loss Calculator

Windows desktop (WPF, .NET 8) Profit & Loss calculator for Card Factory
stores. It signs into the Flooid / Beanstore back office in an embedded
browser, pulls the Refunds/Voids, Branch Performance and Item Sales By
Operator reports, parses them, and computes daily and weekly profit & loss.

## Which store does it use?

**Whichever store the signed-in Flooid account belongs to.** The store is not
configured in the app and is not hardcoded anywhere in the source — Flooid
serves the reports for the account that signed in, and the app reads the
store and the report period back out of the generated report itself. Sign in
as a different store and you get that store's numbers, no changes required.

One thing to be aware of when using this at another store:

- **The gift card product group is currently hardcoded**, as code `600527`
  reached through the hierarchy
  `200005 → 300017 → 400065 → 500303 → 600527`. Product group codes are
  expected to be chain-wide rather than per-store, but this has only been
  confirmed at one store. If gift card retrieval fails elsewhere, check that
  hierarchy first — the app's error message names the exact code it could not
  find. Making this configurable is a sensible future change.

Nothing else in the app is store-specific.

---

## What's in here

```
src/                      application source (5 projects)
tests/                    unit tests for Core and ReportParsing
docs/
  ARCHITECTURE.md         how the app works + verified Flooid facts
tools/                    render-header-logo.py (header artwork)
.github/workflows/        Windows CI build
```

## Building

There is no need for a Windows machine. Pushing to `main` triggers
`.github/workflows/build.yml`, which builds on a GitHub-hosted Windows
runner, runs the tests, and publishes a self-contained `win-x64` build.

Download it from the **latest-test** pre-release on the repository's Releases
page. Each build replaces it.

Locally (on Windows, with the .NET 8 SDK):

```bash
dotnet restore CardFactory.ProfitLoss.sln
dotnet test CardFactory.ProfitLoss.sln --configuration Release
dotnet publish src/CardFactory.ProfitLoss.App/CardFactory.ProfitLoss.App.csproj \
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The machine running the app needs the **Microsoft Edge WebView2 Runtime**.

## Releasing an update

Every push to `main` refreshes the **latest-test** pre-release, for testing. The
app's updater never offers that - only numbered releases. To publish one:

1. Set the new number in `Directory.Build.props` (`Version`, `AssemblyVersion`,
   `FileVersion`), commit and push.
2. Tag that commit with an annotated tag whose message is the "What's new" text,
   and push the tag:

   ```bash
   git tag -a v1.0.1 -m "• Faster Gift Cards retrieval
   • Fix for the back office window size"
   git push origin v1.0.1
   ```

The build checks the tag matches the version, then publishes release `v1.0.1`
with the app and a `.sha256` checksum. Running copies see it at their next launch
(a yellow **Update** button in the title bar), check the download against the
checksum, replace themselves and restart.

---

## Known limitations

- **The gift card product group is hardcoded** as `600527` (see above).
- Requires the **Microsoft Edge WebView2 Runtime**.
- This is automation of a third-party web app with no API. A change to
  Flooid's pages can break retrieval; the error messages name the page and
  field involved.
- Every launch starts signed out. "Stay signed in" keeps the session alive
  (including after Flooid times out) only until the app is closed.

## Diagnostics

The app keeps a log, `CardFactory-PL-diagnostic.txt`, in
`%APPDATA%\CardFactory-ProfitLoss` (type that path into File Explorer's address
bar). It records each sign-in step, every page the Flooid window visits,
per-report timings, and the product group state for Gift Cards - send it with
any fault report. The retrieval speed setting (Fast / Safe) is in the same
folder and is changed from the menu.
