# KillerFind

Fast file search for Windows. Search by filename wildcard or file content across any directory. Streams results in real time with no indexing required.
#### Open-source, GPLv3, run portable or local user install.
#### Landing page is at [KillerFind.net](https://KillerFind.net)
##### Part of [KillerTools.net](https://KillerTools.net).

## Features

- Filename search with wildcard patterns (`*.log`, `report_*.xlsx`, etc.)
- Content search streams through files line by line without loading them into RAM
- Multicore engine: The scan parallelizes across every CPU core
- Multiple search terms in a single pass, each independently tracked
- Filters by extension, date modified, and size, plus include/exclude patterns and a case-sensitive toggle
- Tabbed searches: Each tab is a full independent search; drag to reorder, optionally restored on the next launch
- Search within results: Pipe one search's results into a new tab and drill deeper
- Sort results by name, location, size, or modified date; Ctrl+F filters the list live<br>*(Yo dawg, I heard you liked search...)*
- Results show matched lines with line numbers; open files directly or reveal them in Explorer
- Export to a self-contained HTML report (with its own theme switcher) or CSV
- Six killer themes with live accent colors; UI localized in 8 languages
- Run portable or use the built-in per-user installer (`/silent` installs machine-wide for winget/RMM)

## Requirements

- Windows 10 or 11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included in Windows 10 1903+ and Windows 11)

## Build

Open `KillerFind.sln` in Visual Studio 2022 and build. No external dependencies beyond the NuGet packages in the project file.

## License

GPL-3.0 - see [LICENSE](LICENSE).
