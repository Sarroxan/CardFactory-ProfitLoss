# Status

What is confirmed working, what is built but not yet confirmed, and what is waiting.
Update this whenever something is confirmed or found broken, so no one repeats work.

Owner's rule: features and fixes are gathered first. Nothing is pushed to `main`, built
or released without the owner's say-so. A push to `main` starts a build.

## Released

**v1.2.0** (12 Oct 2026, from `dad5962`) - latest. Zip and `.sha256` attached, notes set.
Confirmed by the owner in the app: Reports section (voids, returns, no sales per person,
£20+ highlighted), staff discounts in Reports, top performers strip, header freshness,
hourly bars, colours, version chip. Figures cross-check on the owner's screenshots: Team
totals £898.65 / 176 transactions match Branch; ABV and AUB arithmetic correct.

v1.1.0 is still listed. Under the README's milestone rule, older releases are removed only
after the owner approves the list - not yet asked for.

Known in v1.2.0 (fixed on `v1.3.0-work`, below): the calendar date range, Targets section
and strip-size fix from `next-update` were never merged; the dashboard changes size when
switching sections; the section-bar titles are blurry.

## Released - v1.4.0 (15 Oct 2026, from `67b2de4`), latest

Published by the owner with Actions -> Publish release (run 38767312139); release build 38767343419 green. Annotated tag by "Card Factory Profit and Loss"; zip (64.6 MB, checksum matches, exe reports 1.4.0) and `.sha256` attached; six-line notes. v1.3.5, v1.3.0 and v1.2.0 still listed.

Tried on a till by the owner (15 Oct): "Works perfectly". Merged into `main` as 1.4.0 for release.

Test build run 38633642006 (from 3467ce8, 14 Oct): green, compiles, only annotation the size notice (61.6 MB). Not yet tried on a till. Version number still 1.3.5.

| Change | Proven | Check in the app |
|--------|--------|------------------|
| **Export to Daily Briefing** (concept M1): a yellow card at the top of the Menu, "Pulls every report for Mon 12/10 and fills it in". Pulls Team, Gift Cards, Branch and Staff Discounts for the day (overlay: "Getting the Daily Briefing ready"), then opens the filled-in sheet in a preview with **Print** and **Save as PDF**. Not signed in: sign in and it carries on. Range mode: refuses with a status line. A failed pull leaves the error up and opens nothing. Replaces the Menu's "Print Daily Briefing" row. | XAML parses, handlers and names match; not compiled yet | Card look; pull from each section; signed-out start; preview fits the screen; Print; Save as PDF (Windows asks for the file name) |
| **Menu redesign P3**: navy band (Menu, store, version state as a pill), tabs on the left - Daily Briefing, Save & Share, Settings - and only the open tab's items. Renamed: Open a saved day, Save this day, Export as a web page, Print summary, Flooid speed (Fast/Safe), Store & defaults, Check for updates, Start fresh; each with a grey line saying what it does. Opens on Daily Briefing; in range mode its Export button is greyed with a note. Same handlers as before. | XAML parses, names and handlers match; not compiled yet | Menu opens under the Menu button and fits; each tab; every item still works; speed switch; version pill colours |
| **Refresh as the end of the date card (R1)**: DAY · date · yellow Refresh (Generate reports on Reports), one unit; text 11px (was 8.5). New DateCardActionButtonStyle. | XAML parses; not compiled | Looks joined in Single day and Date range; Reports label and mark; hover |
| **Deep check fixes** (owner approved all 7): (1) status note - every SetStatus now shows as a navy note at the bottom (green tick, or yellow "!" for problems; 4 s / 7 s; not while the retrieval box is up; section switches and the startup line stay quiet); (2) navy tooltips app-wide; (3) rounded blue tick boxes on the Flooid sign-in; (4) slim themed scrollbar on Reports, update notes, sign-in store list, Daily Briefing preview; (5) save/open failures use the app's message box, not Windows'; (6) nothing under 9.5px (was 5.7-9; the ABV/AUB warning is now "Needed for ABV/AUB" with the full sentence as its tooltip; the "i" marks stay 8px); (7) Store & defaults wording. | XAML parses, names and handlers match; not compiled | Note appears after Save / Print / Export and fades; tooltips; tick boxes; scrollbars; Branch totals bar and gift-card cells still fit at 9.5px in Date range (tightest spot: GIFT SALES over £2,000) |

## Released - v1.3.5 (14 Oct 2026, from `88810e7`), latest

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

## Released - v1.3.0 (13 Oct 2026, from `a509c48`)

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

## Environment notes

- Unit tests cannot run in the cloud sandbox (NuGet is blocked there); CI runs them.
- The cloud session cannot push tags or create/edit/delete releases. Releases are made by
  the owner with the Publish release button (or git on their PC).
- The repository is public; the updater relies on that. No passwords, tokens, real staff
  names or store details are in it; test fixtures use made-up names. Don't commit real
  report saves.
- The project instructions mention `docs/TESTING.md`, `harness/harness.js` and
  `flooid-real-pages/`; none exist in this repository.
