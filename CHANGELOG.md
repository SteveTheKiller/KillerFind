# Changelog

All notable changes to KillerFind are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
