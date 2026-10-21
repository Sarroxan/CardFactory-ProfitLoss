# Status

What is confirmed working, what is built but not yet confirmed, and what is waiting.
Update this whenever something is confirmed or found broken, so no one repeats work.

Owner's rule: features and fixes are gathered first. Nothing is pushed to `main`, built
or released without the owner's say-so. A push to `main` starts a build.

## On branch `briefing-green` (owner, 21 Oct 2026) - not built yet

| Change | Proven | Check |
|--------|--------|-------|
| **Print summary opens a preview first** (owner, 21 Oct 2026: "doesn't show a print preview", concept V1). It went straight to Windows' printer box. Now it opens the Daily Briefing's preview window (Fit page / 100% / − +, page arrows, Print, Save as PDF), which takes any pages: `DailyBriefingPreviewWindow(title, what, jobName, orientation, build)`; the briefing uses it unchanged. The summary shows on A4 portrait and is re-flowed to the chosen printer's printable area when printed, as before (`SummaryPages`). | Not compiled yet | Menu → Print summary: preview window with the summary; Print → printer box → prints as before; Save as PDF works; Daily Briefing preview unchanged |
| **Print & Share: the two sheets as strips** (owner, 21 Oct 2026, concept B2). Daily Briefing and Team Scorecard each a full-width strip with a coloured edge (yellow / blue), an icon tile, what it covers ("The store's sheet · Wed 21/10", "The team on one page · Wed 21/10/2026" or the week) and a yellow **Open** button; on a date range the Briefing's Open greys out with "For one day · choose Single day…". Print summary stays a row underneath. | Not compiled yet | Menu → Print & Share: two strips with Open buttons; on a week the Briefing's Open is greyed |
| **Menu: two tabs** (owner, 21 Oct 2026, concept S3). "Open a saved day" and "Save this day" removed (the .cfpl save/open handlers with them; days saved earlier can no longer be opened). "Save & Share" becomes **Print & Share** (printer icon), the tab the menu opens on, holding everything that is printed: **Daily Briefing** (a row now - greyed with "For one day · choose Single day" on a date range, as the old card's button was), **Team Scorecard**, **Print summary**. The Daily Briefing tab and its card are gone; Settings unchanged. | Not compiled yet | Menu: tabs Print & Share and Settings; Print & Share lists Daily Briefing, Team Scorecard, Print summary; on a week, Daily Briefing greyed |
| **Team Scorecard** (owner, 21 Oct 2026; first named "Performance Sheet", renamed the same day) replaces "Export as a web page" in Save & Share. One A4 page in the app's look, opened at once as a PDF in the browser (no save box): brand band with store, "Team Scorecard" and the day; Sales / ABV / AUB / P&L tiles against target (the app's own ▲/▼ texts and states); Top Performers (the app's chips); Team with red/green ABV, AUB and P&L per person and a totals row; Branch by hour (★ best hour, yellow % bars) beside Reports (voids/returns flagged at £20+, no sales, staff discount, stamps sold, and who was flagged); a lined Notes box filling the rest of the page; no Gift Cards section (concept P1). On a date range it becomes the **Weekly Team Scorecard**: the hours are replaced by **Day by day** (sales, transactions, Flooid's ABV, % of the week, ★ best day) (concept W1) - for that, Refresh in date-range mode now pulls Branch Performance for the whole range; the Branch section still shows the selected day. PDF made by a separate hidden WebView2 (own data folder, so the Flooid sign-in is untouched), backgrounds on, no header/footer, opened with the PDF viewer; temp files over a day old tidied. `Services/TeamScorecard.cs`, `Services/ReportPdf.cs`; the old `BuildReportHtml` removed. | Week reader tested on the owner's real 14-20 Oct Branch save (anonymised fixture: 7 days, totals, period 8,141.83). Not compiled; the PDF and the week pull can't be tried here | Menu → Save & Share → Team Scorecard: one page, as the concept. Calendar on a week → Refresh → Team Scorecard: "Weekly Team Scorecard", Day by day filled |
| **Daily Briefing: beaten ABV/AUB hours in solid green** (concept G2). Hourly ABV and AUB boxes above the day's target are filled green (the Actual boxes' green, #2FB866) with white figures, instead of a yellow ring. Best sales hour now solid yellow with navy figures and a ★ before the figure (concept B2), instead of navy with a yellow edge. | Not compiled yet | Export a day with ABV/AUB targets: hours above target green with white figures; best sales hour yellow "★ £…" |

## Released - v1.0.2 (21 Oct 2026, owner's say-so), from branch `back-office-after-timeout`

Test build 39590725109 (version 1.0.2) green, tests passed. Small-update notice from 1.0.1: 78.98 MB in 240 parts (over the 60% limit, so tills take the full zip once) - very likely the .NET 8 runtime moving 8.0.31 -> 8.0.32 (October 2026 security update).

| Change | Proven | Check |
|--------|--------|-------|
| **Stamps sold** (owner, 21 Oct 2026, concepts R1 + B1). Pulled with Staff Discounts (Generate reports, the full pull from Reports, and the Daily Briefing) from Item Sales By Operator with product group Stamps: the gift card pull now takes its group (`ItemSalesGroup`; Stamps = 200005 > 300017 > 400065 > 500300 > 600561, from saved pickers 16 and 17), and the write-back check refuses a form holding the OTHER group's leaf (600527 / 600561), so the two can never mix. Reader: `GiftCardReportParser("Stamps")`; `StampsSold` counts by name (1st/First, 2nd/Second, Large wins; a book is 1; Christmas/Xmas not recorded at all; other names in Other). Reports: fifth tile STAMPS SOLD. Daily Briefing page 2: only Stamp Count's Sold boxes. Retrieval box: a "Stamps sold" step (5; last year's figures now 6). A stamps problem never stops a pull: "—", blank Sold boxes, a note on the status line and the log. | Reader + counting tested on the owner's real report (anonymised) and made-up names. The Flooid drill is the gift card one with the saved codes; not run on a till. A day with NO stamps sold hasn't been seen - it may show "—" until a save of one is added. Not compiled yet | Reports → Generate for 19/10: tile 1 · "1st 0 · 2nd 1 · Large 0"; Daily Briefing page 2 Sold 0 / 1 / 0. Gift Cards on Team unchanged. A day with no stamps: note what shows |
| **Retrieval box progress line** (owner, 19 Oct 2026: "the yellow line at the bottom isn't aligned", concept P1). It was a square 4px strip along the card's rounded bottom edge (ClipToBounds clips square), so its ends ran past the corners. Now a 6px rounded bar inside the box, 14px in from each side like the steps, 14px above the bottom. Width still set from the track in SetRetrievalStep. | Not compiled yet | Refresh: a rounded yellow bar under the steps fills step by step; nothing sticks out of the corners |
| **Sales target ▲/▼ slow to appear** (owner, 19 Oct 2026: "still takes a long time … tried clicking outside the box"). The target boxes only committed text already in its tidy form ("1450"); "1,450", "£1,450.00" or "1450.00" waited until the box lost keyboard focus, and clicking an empty part of the window doesn't take focus. Now any number commits on each keystroke (`Core/Services/TargetEntry`, used by `ZeroBlankDecimalConverter`), and `FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false` (App.OnStartup) keeps the typed text on screen, which was the reason for holding back (Stage 6A.69). Leaving the box still tidies the text. Only the three target boxes convert text. | TargetEntry unit tests. The WPF setting can't be tried offline. Not compiled yet | Type 1,450.00 in Sales target: ▲/▼ updates with each key, decimals can be typed, text tidies to 1450 on leaving the box; same for ABV 7.20 and AUB 3.1 |
| **Back office after the session has run out** ("when trying to open Flooid back office and it times out it regenerates the whole report"). Opening the back office with an expired session lands on the sign-in; signing in (or Stay signed in doing it) counted as a fresh sign in: the window was hidden, every report pulled again, and since 1.0.1 the held reports were cleared first, so all of them were pulled from Flooid. Now, while the back office is asked for (`FlooidLoginWindow.BackOfficeRequested`), a sign in neither hides the window nor pulls reports, and the window is resizable again after the card. Held reports are kept across a sign in; they are forgotten on Sign out, Change store, or when a different store is read after sign in (`ForgetPulledReports`). | Code reading only: sign-in flow can't run offline. Not compiled yet | Leave the app until the Flooid session runs out, then Open Flooid back office: sign in (or let it sign in itself) → the back office shows and stays; no retrieval box. Then Daily Briefing for a past day: no Team/Gift/Branch pulls |

## Released - v1.0.1 (19 Oct 2026, owner's say-so), from branch `back-office-stays-open` - the first small update

Test build 39266721174 (version 1.0.1) green: tests passed; small update from V1 1.22 MB in 10 parts.

| Change | Proven | Check |
|--------|--------|-------|
| **Open Flooid back office closed the window and refreshed everything.** Every navigation first publishes "Opening Flooid…" (NavigationStarting, state NotInitialised); the main window took that as having left signed in, so the page arriving next looked like a fresh sign in: `HideAfterSignIn()` plus a full report pull (only skipped within a minute of the last automatic pull). Opening the back office navigates to its home page, so it happened on opening, and on any link clicked in it. Now `FlooidSignInTracker` (Flooid project) decides: a page still loading, or the bare `/backoffice/` entry on its way, doesn't change "was signed in"; the login page does, so signing in, and signing in again after the session runs out, still pull the reports by themselves. | Unit tests replay the published sequence (sign in, open back office, a link, session running out). Not compiled yet | Signed in, account menu → Open Flooid back office: the window stays open on the back office, no retrieval starts; click a few menu links a minute later: still open. Sign out and in: reports pulled as before |
| **Fewer pulls from Flooid** (owner, 19 Oct 2026, option 1). Signing in, then opening the Daily Briefing or Reports, pulled the same reports again (9 pulls where 4 would do). Each report is now kept in memory with its period and when it was pulled (`Core/Services/ReportCache`); Daily Briefing and Generate reports use the copy when it was pulled after the period ended (a finished day can't change) or within the last 10 minutes (today). **Refresh always pulls fresh.** Cleared on every new sign in (another store may follow; last year's cache too). "Updated … ago" shows the oldest copy used. Each reuse is written to the diagnostic log ("used the copy pulled at …"). | ReportCache unit tests (past day, today's 10 minutes, a day pulled while still trading, other report/period, clear). Not compiled yet | Sign in, then Export to Daily Briefing: only Staff Discounts is pulled; Reports → Generate: nothing pulled; Refresh: all pulled again. Log shows "used the copy" lines |
| **Daily Briefing: ABV and AUB rings, best hour in navy** (owner, 16 Oct 2026: concepts A1 + S2). Every hourly ABV box above the day's ABV target, and every AUB box above the AUB target, gets the yellow ring the best sales hour used to have (no target set: no rings; equal to target: no ring). The best sales hour is now filled navy with white figures and keeps a yellow edge, so it stands apart from the rings and stays dark on a black-and-white printer. `DailyBriefingSheet.BeatsTarget`. Nothing else on the sheet changes. | Not compiled yet | Export to Daily Briefing for a day with ABV/AUB targets: rings only on hours above target; best sales hour navy |

V1 release record: main build 38950897056 (rehearsal 0.86 MB in 9 parts); Remove release
38951210450 (v1.0.1) and 38951244716 (old v1.0.0); Publish 38951293932; release build
38951311672. Checked: only release, tag by Card Factory Profit and Loss → 9d0d0d6, four
assets, zip checksum matches, piece list = the exe in the zip, exe 1.0.0+9d0d0d6, notes as
sent, latest; live small update from the first 1.0.0 exe 1.24 MB in 8 parts, identical.
`v1-standalone` removed (run 38951885726).

## V1: version 1.0.0, the only release (owner, 16 Oct 2026, ~19:55)

The owner asked for the 1.0.1 build to become V1, numbered 1.0.0, as the only release: the
1.0.1 and earlier 1.0.0 releases are removed. Everything below (to the first 1.0.0 entry) is
in it. Tills on any earlier 1.0.0 or 1.0.1 need V1 installed by hand once (the updater only
installs a higher number); from 1.0.1 onwards every update reaches them by itself, as a
small update.

Before that, 16 Oct ~19:32: small updates went out as a replacement 1.0.1 (main build
38947681439 green, notice "from the previous release: 1.12 MB in 8 parts, rebuilt and
checked"; Remove release 38947998525; Publish 38948035688; release build 38948055559; four
assets; live check from the sandbox: from the real 1.0.0 exe, 1.24 MB in 8 Range requests,
rebuilt file identical to the release). `small-updates` removed (run 38948485588).

### Small updates
| Change | Proven | Check |
|--------|--------|-------|
| **Small updates** (owner, 16 Oct 2026: "is there a faster way"; plan B "by itself" chosen). Every update downloaded the whole 61.6 MB zip, but between the real 1.0.0 and 1.0.1 app files only **1.2 MB in 8 stretches** changed (measured on the two released exes with the same piece cutting). Now each release also carries the app file on its own (`CardFactory-ProfitLoss-win-x64.exe`) and its piece list (`…exe.json`: content-defined pieces 8/32/128 KB, gear hash, each with a 128-bit SHA-256 prefix, plus the whole file's SHA-256). The updater (1.0.2 on) reads the list as soon as the update box opens, cuts its own running file the same way, and downloads only the missing pieces with HTTP Range (GitHub answers 206 - checked); it rebuilds the new file, checks every piece and the whole fingerprint, then swaps it in exactly as before. Falls back to the full zip if the release has no list (older releases), the saving is under 40% (e.g. after a .NET security update), the server ignores Range, or anything fails or doesn't match. The update box shows the size first: green "Small update: only the changes · 1.2 MB" or amber "Full download this time · 61.6 MB" (concept U1); step 1 reads "Downloading the changes". The app stays one file in the same place - no folder, nothing to do on the tills. Logic in Core (`Updates/PieceUpdate.cs`) with unit tests; `tools/MakeUpdateManifest` makes the list in the build and, on every build, rehearses a till's small update from the latest release's exe (rebuild + byte-for-byte check) and reports the size as a build notice. | Not compiled; YAML valid; cutting and the 1.2 MB figure measured on the real exes with an identical C prototype | Build notice "Small updates: … from the previous release: X MB …". On a till: a till installs V1 by hand once; the next release (1.0.1) should show the green "Small update" pill and finish in seconds |

## Released - v1.0.1 (16 Oct 2026, owner's say-so), from branch `chip-v2v4` (three changes)

Main build 38944783933 green (size notice only); Publish release run 38945093086; release build 38945114805 green. Zip 64,612,573 bytes, sha256 bc32ff79...e6d9 matches the `.sha256`, exe ProductVersion 1.0.1+48cb281, marked latest, notes as sent. `chip-v2v4` removed with Remove branch (run 38945488137). Not yet tried on a till.

Version set to 1.0.1 (owner, 16 Oct 2026), so tills on 1.0.0 update by themselves. Test build run 38944191253 (from d8db0ac): green, tests pass (including the new no-discounts test), only annotation the size notice (61.6 MB). Not yet tried on a till. Also: a
parser test for a real day with no discounts (fixture DiscountsAndPriceOverrides_NoDiscounts_2026-10-15.html,
anonymised like the others).


| Change | Proven | Check in the app |
|--------|--------|------------------|
| **Wrong password shown at once** (owner, 16 Oct 2026: "it just sticks to trying to sign in"): after Login the app now reads Flooid's own error box every half second - from the saved wrong-password page: Flooid returns to /authserver/login?loginError=error.invalidUser with `div#loginError` "Invalid user logon details provided" - and shows it on the card straight away: "Flooid says: Invalid user logon details provided. Check your username and password and try again." Before, nothing looked for that box, so the card sat on "Signing in…" until the 20 second limit. The submit script removes an old error box before clicking, so a second try is never read as refused early. A refused saved (automatic) sign in is forgotten, as before, with Flooid's message. The 20 second fallback stays for anything else. | Not compiled; matches the saved page 13 | A wrong password: message within a second or two, password box cleared, window grows for the message; then the right password signs in; a refused saved sign in is forgotten |
| **Daily Briefing preview D3** (owner, 16 Oct 2026: "very big ... very zoomed in", chosen from D1-D3): the preview fitted the page to the window's WIDTH, so on a till screen the page was taller than the window - zoomed in, the bottom third (staff discounts, Team Performance) cut off. Now one page at a time in a DocumentPageView: **Fit page** (default, every time it opens) shrinks the whole sheet into the window, no scroll bars; a zoom switch in the navy band (− / Fit page / 100% / +, steps 50-200%) shows it bigger with scroll bars. "‹ Page 1 of 2 ›" arrows at the bottom; in Fit page the mouse wheel and Left/Right also turn the page (PageUp/PageDown always). Window about 90% of the screen above the taskbar (max 1600x1100), centred on the app. Print and Save as PDF unchanged (they build a fresh copy of the sheet). | Not compiled; XAML well-formed | Whole page visible on opening; page 2 by arrow, wheel and keys; 100% and +/− with scrolling; Fit page brings it back; Print and Save as PDF still work |
| **Version chip V2 + V4** (owner, 16 Oct 2026, from concepts V1-V4): a round 18px badge carries the state as a symbol and a colour - grey circling arrows checking, green tick up to date, yellow badge with a navy down-arrow update ready, red "!" can't check - then the version in Fredoka (CfLogoSmallFont) and the state in small words ("up to date", "ready to install", "can't check", "checking"). Outline CfLine (softer), so the navy Menu pill stays the main button; ready-to-install keeps the yellow outline on pale yellow. V4: while the update box downloads, the chip (held at its width) fills with yellow from the left and shows the percentage with "downloading", then "installing…"; Later or a failed install puts it back to "ready to install". UpdateWindow passes its progress to the chip (new optional onProgress). Menu band pill text unchanged in meaning. | Not compiled; XAML well-formed | Each state: launch (checking → up to date), unplug network and click (can't check), an update waiting (ready to install, hover); Update now: the chip fills and counts up behind the box, then installing…; Later restores it |

## Released - v1.0.0 replaced again 16 Oct 2026, 17:52, from `c791201` (owner's say-so)

The owner asked for v1.0.0 to be replaced with the two changes below (same number, as on
15 Oct) before a test build. Owner checked by hand that Flooid returns last year's reports.
Main build 38934757652 green first (compile + tests, size notice only), then Remove release
(run 38935065213), Publish release (run 38935124302; V1 notes plus two points), release
build 38935147252 green. Zip 64,617,430 bytes, sha256 a442a11d...ed49 matches the
`.sha256`, exe ProductVersion 1.0.0+c791201, marked latest, tagger "Card Factory Profit and
Loss". `ly-targets` removed with Remove branch (run 38935511979). **Tried on a till by the
owner (16 Oct): "Works perfectly".** Tills on an earlier 1.0.0 need this zip installed by
hand (same number).


| Change | Proven | Check in the app |
|--------|--------|------------------|
| **Sign-in window sized to the card** (owner, 16 Oct 2026, from two till photos): with the S2 card up the window was a fixed 440x430, cutting off Login and the links. Now sized from the card's measured height with an 18px blue gap on all four sides (462 wide), re-sized if the card grows (error message, Signing in…, store list), not resizable while the card shows (ResizeMode NoResize and WindowChrome resize border 0; the Flooid page and back office stay resizable), and centred on the app both ways, kept on screen - also each time the window is shown again. | Not compiled; XAML well-formed | Card fully visible with Login and both links; equal blue gap; edges can't be dragged; centred on the app (move the app, sign out, sign in again); wrong password error grows the window; Show the Flooid page / Back still work |
| **Targets from last year** (owner, 16 Oct 2026, concept T2): on every Refresh (and the Daily Briefing pull) the ABV and AUB targets are filled from last year's figures plus a percentage, default **ABV +10%, AUB +0%**. Last year = the same weekdays 52 weeks back (Fri 16/10/2026 → Fri 17/10/2025; a week → last year's same week; any range shifted 364 days). Last year's ABV/AUB come from the same two reports and calculation as today's Team figures: Refunds, Voids & No Sales + Gift Cards for last year's dates, merged (OperatorReportMerger) and run through TeamPerformanceCalculator, so gift cards are taken out. Last year's figure is rounded to 2 places before the % so the shown sum matches. Pulled once per period per session (cached). Runs after today's reports as step 5 of the Getting reports box; a failure or a closed day leaves the targets as they were and adds a note to the status line (detail in the diagnostic). The Sales target is not touched. Menu → Settings → **Targets from last year** row (note "On · ABV +10% · AUB +0%") opens a window: On/Off, ABV % and AUB % (-50 to +100), and the working out from the last Refresh; Save applies new % to the held figures at once. Settings file CardFactory-PL-lastyear-targets.txt in the app folder. Under the ABV and AUB target boxes a yellow **LY +10%** / **LY +0%** tab shows while the box still holds last year's figure (typing over it removes the tab); hover for the sum. LastYearTargetCalculator (Core) has unit tests. | Not compiled (no .NET in the sandbox); XAML well-formed; Core tests written, run by CI | Owner first: Flooid returns last year's reports (run Refunds or Branch for 17/10/2025 by hand). Then: Refresh fills ABV/AUB with LY tabs; hover sums; a week range; Off; changing % in the window; a closed day last year; Daily Briefing targets |

## On `main` since 15 Oct 2026 (merged from `housekeeping`, owner's say-so) - workflows and docs only, no release needed

Main build 38794532412 (from 2b9ce28) green, only annotation the size notice. `housekeeping` was then removed with Remove branch (run 38794816995, its first use); `briefing-sales` (an unwanted Sales Actual change, discarded at the owner's request) with run 38795515381.

| Change | Proven | Check |
|--------|--------|-------|
| Remove release workflow gets a display name (`name: Remove release`); Actions listed it by its file path | YAML valid | Shows as "Remove release" in Actions once on `main` |
| Publish release and Remove release run on `ubuntu-24.04` instead of `ubuntu-latest`, which GitHub moves to Ubuntu 26 from 19 Oct 2026 | YAML valid | Next release / removal runs as before |
| **Remove branch** workflow (remove-branch.yml; inputs branch and confirm): deletes one branch, only if it is fully merged into main, never main itself. The cloud session's direct branch delete is refused (HTTP 403) | YAML valid | First use on the next merged branch (`title-k3` was deleted by the owner on the website, 15 Oct) |
| STATUS brought up to date (re-publish recorded, v1.2.0 no longer shown as latest, release rights) | - | - |

## Released - v1.0.0 (15 Oct 2026), latest - the owner's fresh start (V1)

**Re-published 15 Oct 2026, 19:36, from `03a81de`** with Title Y1 (below), at the owner's request, keeping the number 1.0.0: Remove release (run 38786657088) deleted the first v1.0.0 release and tag, Publish release (run 38786708374) re-tagged with the same notes, release build 38786732768 green. Zip 64.6 MB, checksum matches the `.sha256`, exe reports 1.0.0, marked latest. Main build 38786294710 green. Tills that installed the first 1.0.0 (before 19:36) do not pick up the replacement by themselves (same number): reinstall from the release zip, or it arrives with the next higher version.

| Change (in the re-publish) | Proven | Check in the app |
|--------|--------|------------------|
| **Title Y1** (owner, 15 Oct; replaces K3, whose "Card Factory" in Chewy 21 had its "y" touching the title): the pre-V1 logo's own sizes and spacing - "Card Factory" Fredoka 14, "Profit & Loss" Chewy 42, CALCULATOR Fredoka 12 between two yellow bars - beside the V1 smiley and badge; tops worked out from the font metrics so the "y" clears the title by 5px. Same on the sign-in banner and the Export / Print header (export embeds Chewy and Fredoka). Headline totals back to Arial Bold; empty-state titles in Chewy. New CfLogoSmallFont (Fredoka) for the logo's small lettering. **Remove release** workflow (remove-release.yml) added: deletes one release and its tag so a version can be published again. | XAML parses, names and handlers match | Header logo, sign-in banner, exported page and Print summary header; totals |

First published 15 Oct 2026 from `631caf3`. Published by the owner with Actions -> Publish release (run 38781787996); release build 38781811685 green. Annotated tag by "Card Factory Profit and Loss"; zip (64.6 MB, checksum matches, exe reports 1.0.0) and `.sha256` attached; notes are the full V1 description. Older releases, tags and branches removed by the owner; The first publish's two runs were deleted from Actions on 15 Oct after the re-publish.

The number goes back to 1.0.0 by the owner's choice. The updater only installs a higher
number than it has, so tills on 1.2.0-1.4.0 will NOT update to 1.0.0 by themselves: each
needs the 1.0.0 zip installed once by hand. From then on 1.0.x / 1.x updates arrive as
usual. The release build marks v1.0.0 as latest (--latest). Older releases (v1.2.0-v1.4.0)
have been removed.

Test build run 38780406386 (from b7e29c0, version 1.0.0): green, only annotation the size notice (61.6 MB). Tried on a till by the owner (15 Oct): "All good". Merged into `main` for release as 1.0.0.

| Change | Proven | Check in the app |
|--------|--------|------------------|
| **Flooid sign-in S2** (owner, 15 Oct): laid out like Flooid's own login page - card on a blue background, navy banner (smiley, "Card Factory P&L", "Sign in with your Flooid account", yellow edge), Username / Password above white fields, the two tick boxes, a yellow Login, "Forgot your password?" (opens Flooid's own page, whose link resets it) and "Show the Flooid page". Fields lose their icons. | XAML parses, names and handlers match; not compiled | Card fits the window; typing and Login; Enter in the password box; error box; Forgot / Show the Flooid page then Back to the simple sign in; the Choose a store step under the same banner |
| **Menu button D1** (owner, 15 Oct): same place; navy pill with "Menu" in the Chewy title lettering; hover adds the yellow ring; while the menu is open it is yellow and reads "Close" with a cross (Tag="Open" from the popup's Opened/Closed). Width 78 (was 68), menu offset moved to match. Pressing Close no longer reopens the menu (300 ms guard). | XAML parses, names and handlers match; not compiled | Rest, hover, open; Close shuts it; clicking elsewhere shuts it and the button goes back to navy; menu lines up under the button |
| **Logo E1 + icon S2** (owner, 15 Oct): header logo (same 330x84 slot) is now the Card Factory smiley (HeaderSmileyFace.png, unchanged) with a white three-bar badge, "CARD FACTORY" in small spaced capitals (plain Arial, not the Card Factory wordmark) over "Profit & Loss" in Fredoka (new CfBrandFont). App icon (all 10 sizes) redrawn as S2 by tools/render-app-icon.py. Sign-in banner uses the same two-line name. The Card Factory wordmark is not used; their brand team would supply it if it is ever to appear. | XAML parses; icon file holds all 10 sizes (checked); not compiled | Header logo fits before the tabs; icon on desktop / taskbar / title bar (Windows may need its icon cache refreshed); sign-in banner |
| **Title-bar icon and version dot** (owner, 15 Oct): the main window's title bar and the Flooid sign-in window show the S2 icon (new Assets/AppIcon.png from tools/render-app-icon.py) instead of the old smiley-on-blue (HeaderSmiley.png, removed). The version chip's three drawn faces are replaced by an 8px dot: grey checking, green up to date, amber update available, red can't check. | XAML parses; no references to HeaderSmiley.png left; not compiled | Title bars; chip in all four states |
| **Polish 3-7** (owner, 15 Oct): (3) Branch's Performance / Gift Cards switch: the tab you are on is yellow with navy text inside a pale card, the other muted - like the header tabs; (4) empty states: Branch (no hours and no error) and Reports (not generated) show the smiley, "No figures yet" / "No reports yet", the period, and a Refresh / Generate reports button; Team is left as is, because people type figures into it by hand; (5) the Getting reports box: navy band with the app icon, title in Chewy, the period under it, steps as circles (pale / yellow running / green done), thin yellow progress line, shadow moved behind the card so its text is sharp; (6) Export (HTML, smiley and Fredoka embedded as data URIs) and Print summary open with the logo band - smiley + bar badge, CARD FACTORY over Profit & Loss, store and dates, yellow rule; (7) headline totals (Team totals, Targets results, Branch totals bar) in Fredoka (HtmlTotalMetricTextStyle), same sizes; Fredoka is narrower than Arial Bold. | XAML parses, names and handlers match; not compiled | Each state of the Branch switch; empty Branch and Reports before a pull and their buttons; the Getting reports box through a full pull, a Reports pull, the Daily Briefing pull and an error; an exported page opened in a browser and emailed; a Print summary; totals |

## Released - v1.4.0 (15 Oct 2026, from `67b2de4`) - release since removed

Published by the owner with Actions -> Publish release (run 38767312139); release build 38767343419 green. Annotated tag by "Card Factory Profit and Loss"; zip (64.6 MB, checksum matches, exe reports 1.4.0) and `.sha256` attached; six-line notes. v1.3.5, v1.3.0 and v1.2.0 still listed.

Tried on a till by the owner (15 Oct): "Works perfectly". Merged into `main` as 1.4.0 for release.

Test build run 38633642006 (from 3467ce8, 14 Oct): green, compiles, only annotation the size notice (61.6 MB). Not yet tried on a till. Version number still 1.3.5.

| Change | Proven | Check in the app |
|--------|--------|------------------|
| **Export to Daily Briefing** (concept M1): a yellow card at the top of the Menu, "Pulls every report for Mon 12/10 and fills it in". Pulls Team, Gift Cards, Branch and Staff Discounts for the day (overlay: "Getting the Daily Briefing ready"), then opens the filled-in sheet in a preview with **Print** and **Save as PDF**. Not signed in: sign in and it carries on. Range mode: refuses with a status line. A failed pull leaves the error up and opens nothing. Replaces the Menu's "Print Daily Briefing" row. | XAML parses, handlers and names match; not compiled yet | Card look; pull from each section; signed-out start; preview fits the screen; Print; Save as PDF (Windows asks for the file name) |
| **Menu redesign P3**: navy band (Menu, store, version state as a pill), tabs on the left - Daily Briefing, Save & Share, Settings - and only the open tab's items. Renamed: Open a saved day, Save this day, Export as a web page, Print summary, Flooid speed (Fast/Safe), Store & defaults, Check for updates, Start fresh; each with a grey line saying what it does. Opens on Daily Briefing; in range mode its Export button is greyed with a note. Same handlers as before. | XAML parses, names and handlers match; not compiled yet | Menu opens under the Menu button and fits; each tab; every item still works; speed switch; version pill colours |
| **Refresh as the end of the date card (R1)**: DAY · date · yellow Refresh (Generate reports on Reports), one unit; text 11px (was 8.5). New DateCardActionButtonStyle. | XAML parses; not compiled | Looks joined in Single day and Date range; Reports label and mark; hover |
| **Deep check fixes** (owner approved all 7): (1) status note - every SetStatus now shows as a navy note at the bottom (green tick, or yellow "!" for problems; 4 s / 7 s; not while the retrieval box is up; section switches and the startup line stay quiet); (2) navy tooltips app-wide; (3) rounded blue tick boxes on the Flooid sign-in; (4) slim themed scrollbar on Reports, update notes, sign-in store list, Daily Briefing preview; (5) save/open failures use the app's message box, not Windows'; (6) nothing under 9.5px (was 5.7-9; the ABV/AUB warning is now "Needed for ABV/AUB" with the full sentence as its tooltip; the "i" marks stay 8px); (7) Store & defaults wording. | XAML parses, names and handlers match; not compiled | Note appears after Save / Print / Export and fades; tooltips; tick boxes; scrollbars; Branch totals bar and gift-card cells still fit at 9.5px in Date range (tightest spot: GIFT SALES over £2,000) |

## Released - v1.3.5 (14 Oct 2026, from `88810e7`) - release since removed

Published by the owner with Actions -> Publish release (run 38560186028); release build
38560203323 green. Annotated tag by "Card Factory Profit and Loss"; zip (61.5 MB) and
`.sha256` attached; notes are the seven points typed in. v1.3.0 and v1.2.0 still listed.

| Change | Proven | Check in the app |
|--------|--------|------------------|
| Print Daily Briefing (Menu): the store's own sheet with the day's figures | Compiles; layout preview from the same box positions | Print to "Microsoft Print to PDF": every figure in its box; a 9:00 day; no Reports; 8+ people |
| Export and Print include Reports, app colours | Compiles; HTML preview | With and without Reports generated |
| New app icon (A3); header logo as shapes; logo square in the header blue | Compiles | Look on a till |
| Smaller download: language packs gone | Build notice: download 61.6 MB (was 69.3), exe 142.3 MB (was 165) | - |
| Windows Forms removal | **Did not work** - the build notice says it is still inside | Owner decided 15 Oct: leave it in, don't try again |
| Crash safety net; one copy at a time; update check every 3 hours | Compiles | Open the app twice; update from 1.3.0 after release |

Daily Briefing Sales Actual uses the app's counted sales (gift cards excluded), as the
Targets section does. Owner, 15 Oct: correct as it is (sales, Sales Actual and ABV checked
day by day and over a week) - do not change.

## Released - v1.3.0 (13 Oct 2026, from `a509c48`) - release since removed

Published by the owner with Actions -> Publish release (run 38325603649), build run
38325613905 green with zero annotations. Annotated tag by "Card Factory Profit and Loss";
zip and `.sha256` attached; notes are the seven points typed in. v1.2.0 and v1.1.0 still
listed - removal of older releases awaits the owner's approval.

**Confirmed by the owner in the app, 13 Oct 2026: v1.3.0 works with no issues** - every
item below, including sections staying the same size and the sharp titles. v1.1.0 and its
tag have been removed (owner, 13 Oct); v1.3.0 and v1.2.0 remain.

The items below are in v1.3.0 (the right-hand column was the checklist).

| # | Change | Proven offline | Checked in the app (all confirmed) |
|---|--------|----------------|---------------------------|
| 1 | Menu > Settings > **Check for updates**, with "v1.3.0 · checked … ago" | Builds | Row looks right; runs the check; outcome in status line |
| 2 | **Version chip** checks again when clicked (Up to date / Can't check), "Checking…" while it runs | Builds | Click → Checking… → result; Update available still opens the box |
| 3 | **Single day / Date range** chosen in the calendar (K1, from `next-update` 218d065) | Merged cleanly | Both modes pull the right dates |
| 4 | **Targets** section, formulas behind (i) (L3, 218d065) | Merged cleanly | Layout as designed |
| 5 | Top performers strip takes its 36px from Team's spare space (b76e079) | Merged cleanly | No shrink after a Refresh |
| 6 | **Generate reports** pulls only Refunds/Voids + Discounts and Price Overrides; Team, Gift Card, Branch figures and both "updated" times untouched; sign-in follow-up pulls the same | Builds | Only 2 steps in the overlay; Team/Branch figures unchanged after it |
| 7 | Button reads **Generate reports** with a report mark on Reports; Refresh elsewhere | Builds | Label, icon and width look right |
| 8 | Dashboard no longer changes size between sections (from 3-5: `TeamSpacerHeight`) | Not provable offline | Team, Branch, Reports same size with 1, 2, 3+ people, before and after Refresh |
| 10 | Reports cleared when the date or range changes away from the one pulled | Builds | Change date → Reports empty with the "Press Generate reports…" line |
| 11 | **Actions → Publish release** button (version + notes → annotated tag → build → release) | YAML valid; notes formatting tested | First real use (v1.3.0) |
| 13 | CI actions moved to Node 24 versions (checkout v5, setup-dotnet v5, upload-artifact v6) | Versions checked | Build log has no Node 20 warning |
| 15 | Section-bar titles are real text in embedded **Chewy** (18px), not resized pictures | Font size matched to old art within 2px wide, 0.5px vertical | Titles sharp, same size and place |

Merge check for 3-5 (item 12): the Reports and Branch panels are byte-identical before and
after the merge; every control the code names exists; every XAML event handler exists.

## Released - v1.2.0 (12 Oct 2026, from `dad5962`) - release since removed

**v1.2.0**:  Zip and `.sha256` attached, notes set.
Confirmed by the owner in the app: Reports section (voids, returns, no sales per person,
£20+ highlighted), staff discounts in Reports, top performers strip, header freshness,
hourly bars, colours, version chip. Figures cross-check on the owner's screenshots: Team
totals £898.65 / 176 transactions match Branch; ABV and AUB arithmetic correct.

v1.1.0 is still listed. Under the README's milestone rule, older releases are removed only
after the owner approves the list - not yet asked for.

Known in v1.2.0 (fixed on `v1.3.0-work`, below): the calendar date range, Targets section
and strip-size fix from `next-update` were never merged; the dashboard changes size when
switching sections; the section-bar titles are blurry.

## Environment notes

- Unit tests cannot run in the cloud sandbox (NuGet is blocked there); CI runs them.
- The cloud session cannot push tags, delete branches, or create/edit/delete releases
  directly (HTTP 403). It runs the Publish release (release.yml) and Remove release
  (remove-release.yml) workflows itself via workflow_dispatch, which do those on GitHub's
  side, and can delete Actions runs. Merged branches are removed with the Remove branch
  workflow (remove-branch.yml).
- The repository is public; the updater relies on that. No passwords, tokens, real staff
  names or store details are in it; test fixtures use made-up names. Don't commit real
  report saves.
- The project instructions mention `docs/TESTING.md` and `harness/harness.js`; neither
  exists in this repository. Real saved Flooid pages (all pages the app uses, 16 Oct 2026,
  anonymised) are in the Claude Project under `flooid-real-pages/`, not in this public repo.
