# Status

What is confirmed working, what is built but not yet confirmed, and what is waiting.
Update this whenever something is confirmed or found broken, so no one repeats work.

Owner's rule: features and fixes are gathered first. Nothing is pushed to `main`, built
or released without the owner's say-so. A push to `main` starts a build.

## On `main` since 15 Oct 2026 (merged from `housekeeping`, owner's say-so) - workflows and docs only, no release needed

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
| Windows Forms removal | **Did not work** - the build notice says it is still inside | Left as is unless the owner asks |
| Crash safety net; one copy at a time; update check every 3 hours | Compiles | Open the app twice; update from 1.3.0 after release |

Daily Briefing Sales Actual uses the app's counted sales (gift cards excluded), as the
Targets section does - owner asked whether to switch to full store sales; not answered yet.

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
- The project instructions mention `docs/TESTING.md`, `harness/harness.js` and
  `flooid-real-pages/`; none exist in this repository.
