# KillerFind

Fast file search for Windows. Search by filename wildcard or file content across any directory. Streams results in real time with no indexing required.

Part of [killertools.net](https://killertools.net).

## Features

- Filename search with wildcard patterns (`*.log`, `report_*.xlsx`, etc.)
- Content search — streams through files line by line without loading them into RAM
- Multiple search terms in a single pass, each independently tracked
- Include/exclude filters with semicolon-separated patterns
- Case sensitive toggle
- Results show matched lines with line numbers, click to reveal the file in Explorer or open it directly
- HTML export of results
- Dark terminal UI, no installer required

## Requirements

- Windows 10 or 11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included in Windows 10 1903+ and Windows 11)

## Build

Open `KillerFind.sln` in Visual Studio 2022 and build. No external dependencies beyond the NuGet packages in the project file.

## License

GPL-3.0 — see [LICENSE](LICENSE).
