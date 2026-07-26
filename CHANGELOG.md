# Changelog

All notable changes to KillerFind are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - Unreleased

### Added
- **Browse mode.** A tab opens at Home and lists what is there; double-click enters a folder or opens a file. Browsed entries land in the same collection as search results, so every view, sort, filter and command works on them unchanged. Listing runs off the UI thread.
- **Folder tree** down the left, rooted at This PC, open by default. Children load on expand, the tree follows wherever you navigate, and its width is draggable and remembered.
- **Address bar** with Back, Forward and Up, tracked per tab. Click it, Ctrl+L or Alt+D to type a path; Ctrl+O still opens the folder picker.
- **Favorites** drawer under the tree. Rail star or Ctrl+B to open, Alt+1 to Alt+0 to jump to the first ten, drop a folder on it to save it.
- **Three view modes**: list (expandable cards), icons (32px to 256px tiles) and details (sortable columns). Icons run on a virtualizing wrap panel, so a 100,000-hit search in tiles stays usable.
- **The real Windows context menu**, under "More Windows options...", plus Properties. Shell extensions, Send To and the full Open With list all included, drawn as the native Win32 menu.
- **New context-menu commands**: copy the full path, file name, folder path, matched lines, the file itself or a SHA-256; open as administrator; search inside this folder; exclude this folder. All act on the whole selection.
- **Marquee selection and drag out.** Drag from an item to drop those files into Explorer or a mail client; drag from empty space to rubber-band a selection.
- **Show hidden items** and **keep folders above files** toolbar toggles, both remembered.
- **Czech and Japanese**, taking the UI to ten languages.
- **Install for all users** checkbox on the install prompt, putting KillerFind in Program Files with a Start Menu and Add/Remove Programs entry for every account. It routes through the same machine-wide `/silent` install winget, choco and RMMs already use, so UAC appears only when the box is ticked. Installing this way removes an existing per-user copy, so there is only ever one install and one uninstall entry, and your theme, accent, language and tabs are kept. Pre-ticked and locked if KillerFind is already installed machine-wide.
- WinGet submission workflow (`.github/workflows/winget-release.yml`): publishing a GitHub release submits the new version to winget-pkgs via komac, the same way KillerScan does it. The first submission still has to be made by hand with `komac new`.
- **Release date in the About card**, muted and italic beside the version, so a build's age is visible. It comes from a new `<ReleaseDate>` field in the csproj, baked into the assembly as metadata so it survives being copied around, and `release.ps1` fails the release if it does not match this version's CHANGELOG date. Older builds without the field just show the version.
- The signer's alias under the publisher in the About card: `AKA "Steve the Killer"`, shown only when the exe is signed by Stephen Riley. Unsigned builds and forks signed by anyone else do not get the line.
- `--demo` also renders the About card in its signed state (publisher, thumbprint, alias), so marketing captures taken from an unsigned local build match the released one. The values are the real certificate's, not invented.
- **App-wide accessibility zoom**: roll the wheel over the title-bar wordmark to resize the whole app, about 2% per notch between 70% and 250%, remembered across restarts. It is a `LayoutTransform`, so text reflows and re-rasterizes crisply rather than being bitmap-stretched, and the title bar and footer stay put so the wordmark never moves under the pointer. The wordmark also drags the window and double-clicks to maximize; its old link to killerfind.net moved to the About card.
- **F1 opens a shortcuts card** listing every gesture in one place, translated into all ten languages. The pattern cheat-sheet is still one click away on its own button. New with it: F5 runs the search (Enter still does too), Ctrl+E exports HTML, Ctrl+Shift+E exports CSV, Ctrl+Right and Ctrl+Left expand and collapse every result (skipped inside a text box), Ctrl+Shift+F pipes the results into a new tab, Ctrl+Shift+L clears them, and Ctrl+Shift+C toggles case-sensitive matching.

### Changed
- The search panel moved to the right edge and starts closed; the left is the folder tree's now. The rail chevron or Ctrl+Shift+S opens it, and the choice is remembered.
- Browsed folders track the disk. The active tab is watched, so a file deleted in another window disappears here too. Events are debounced, and a burst past 200 paths relists instead of patching entry by entry.
- Details view hides the location column while browsing, where it repeats the same folder on every row. Search results keep it.
- The About card now matches KillerScan, KillerNotes and KillerPDF: the family's matte pane brush, field spacing and type sizes, EXE SHA-256 as the hash label in every language, a wrapping thumbprint and no duplicate copyright footer. The tagline reads "Fast file search for Windows. Names, contents, wildcards and filters, with no index. A GPLv3 Killer Tools utility.", with Killer Tools linking to killertools.net and GPLv3 and the product name composed in the layout rather than baked into each translation. The card's width now comes from the SHA-256 line instead of a fixed 468px, so a long tagline wraps rather than widening it.
- `release.ps1` brought up to the family standard. It was build / sign / hash / print; it now runs a preflight (default branch, clean tree, in sync with origin, tag free locally and remotely, CHANGELOG section dated), scans for vulnerable packages, checks the built FileVersion against the csproj, tries three timestamp authorities and gates on `signtool verify /pa`, rewrites the `find-landing` hero block, the verEgg footers and the README source-zip link, then tags and publishes the release with the exe, source zip and SHA256SUMS.txt. `-DryRun` skips the pushes and leaves the working tree untouched.

### Fixed
- "Keep folders above files" did nothing. Listings enumerated folders and then files, so the default "as found" order was already grouped and turning the toggle off changed nothing visible. One interleaved pass now, with the grouping left to the view.
- File-type icons were slightly soft, and visibly blurry once the app-wide zoom was turned up. `IconCache` asked the shell for the SMALL icon (16px) while the row draws it at 18px, so it was stretched even at 100%. It fetches the 32px icon now.
- The separator in the title-bar system menu drew as Windows' stock bright gray 3D line instead of the themed hairline. Inside a menu WPF resolves `MenuItem.SeparatorStyleKey` rather than the app's implicit `Separator` style, and that key was never registered, so every themed menu separator fell back to Aero. Registered now, as it already is in KillerScan and KillerNotes.
- The README had no GPL3 corresponding-source link. `release.ps1` rewrites that link on every release, but there was nothing for its pattern to match, so the step reported success while doing nothing and the published binary shipped with no link to its source. The Download section carries it now.
- Spanish, French and Turkish were missing every accented character, having been authored as pure ASCII ("busqueda", "Demarrer", "kullanici"). All three have been rewritten with proper text.
- `+ add group` was never translated: the key existed in `en-US.xaml` and in no other locale file, so every non-English user saw the English label. All locales sit at full key parity now.
- The in-app updater silently did nothing on a machine-wide install. Program Files is not writable by a normal user, and the update helper ran unelevated with no errorlevel check, so the copy failed and it relaunched the *old* exe with no error shown. It now checks the copy, elevates when the target needs it (one UAC prompt), opens the releases page on a genuine failure, and relaunches via Explorer so the app returns at normal integrity rather than continuing as administrator.
- A search that matched a lot of files made the window unresponsive. The engine drained its whole result queue into one dispatcher callback every 150ms, and a callback cannot be interrupted once it has started, so a broad term became one uninterruptible block of work with no slot for input. Batches are capped at 250 now, with a backlog drained by posting slices back to back, so throughput is the same and only the length of any one callback is bounded.
- The app-size readout parked itself on the status bar at any scale other than 100%, and repainted on every launch. It is transient now: each wheel notch shows the percentage and restarts a five second timer. A scale restored at startup applies silently, and rolling back to exactly 100% clears it at once.
- Sorting could stall the window on a search that returned a lot of hits. The sort lived on the collection view, so every arriving result was a binary search plus an insert into the middle of the list, and tens of thousands of them worked out to roughly a billion element moves on the UI thread. The sort is suspended for the length of a run and applied once at the end; results stream in discovery order while the search is live, and changing the sort mid-run reorders on completion.

## [1.0.0] - 2026-07-04

### Added
- Tabs: each tab is a full independent search (folder, terms, filters, results); drag to reorder, optionally remembered across restarts.
- Filters: extension / date modified / size rows, AND-ed with the search terms.
- Search within results: pipe one search's results into a new tab and drill deeper (funnel button or tab right-click).
- Ctrl+F quick filter over results, plus in-app sorting by name / location / size / modified.
- CSV export alongside the HTML report; the report gained the six-theme + accent switcher and sortable columns.
- Explorer-style folder picker: quick places, list / icons / details views, sortable columns, remembers size and placement.
- Per-user installer with PORTABLE badge, plus `/silent` (machine-wide) and `/uninstall`.
- Smart Esc: closes popups, stops a running search, then offers to quit (choice can be remembered).
- Pattern cheat-sheet card; plain-text patterns now just work (`log` matches `*log*`).
- UI in 8 languages (en, es, de, fr, tr, zh-CN, zh-TW, bn).
- `--demo` mode with fabricated results for screenshots. You think I'm gonna let you see my **real** files?

### Changed
- Reskin to the shared KillerUI "Grunge" theme: custom chrome, multi-theme switcher with Dark/Blue default, film grain, typewriter wordmark, and a UI refresh throughout (real file icons, collapsed-by-default results, themed dialogs and menus).
- Multicore search engine: the scan parallelizes across cores with precompiled patterns; the window stays responsive mid-search.
- Searching with no folder selected opens the folder picker instead of an error.

### Fixed
- Name terms without wildcards now match as contains instead of silently matching nothing.

## [0.1.1] - 2026-05-04

### Added
- Code signing on the release binary (Certum).
- `find-landing` marketing site.

## [0.1.0] - 2026-04-26

### Added
- Initial release. Fast file search for Windows with no indexing:
  - Filename search with wildcard patterns (`*.log`, `report_*.xlsx`).
  - Content search that streams files line by line without loading them into RAM, skipping likely-binary files (null byte in the first 4 KB).
  - Multiple search terms in a single pass, each independently tracked.
  - Include / exclude filters with semicolon-separated patterns (`*.txt;*.log`, `bin;obj;*.min.js`).
  - Case-sensitive toggle.
  - Real-time streaming results (batched every ~150ms) with matched line numbers; click a result to open the file or reveal it in Explorer.
  - HTML export of results.
  - Dark terminal-style UI, single portable exe, no installer.
- Targets .NET Framework 4.8 (x64). GPL-3.0 licensed.

[1.0.0]: https://github.com/SteveTheKiller/KillerFind/compare/v0.1.1...v1.0.0
[0.1.1]: https://github.com/SteveTheKiller/KillerFind/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/SteveTheKiller/KillerFind/releases/tag/v0.1.0
