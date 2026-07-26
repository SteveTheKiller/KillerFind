<p align="center">
  <a href="https://killerfind.net"><img src="docs/wordmark.png" width="640" alt="KillerFind - Free File Search Browser"></a>
</p>

A file browser for Windows built around search. Browse folders in list, icon, or details view with tabs, or search any directory by filename wildcard or file content, streaming results in real time with no indexing required. Every result is a real file: open it, rename it, move it, or pipe the whole set into another search.
#### Open-source, GPLv3, run portable or install for just you or every user on the PC.
##### Part of [KillerTools.net](https://KillerTools.net).

## Features

- Browse any folder in list, icon, or details view, with a folder tree, an address bar, and back / forward / up on Explorer's own keys
- File operations on results and folders alike: copy, cut, paste, rename, delete to the recycle bin or permanently, new folder, plus two-way drag and drop with Explorer
- Right-click anything for the full Windows shell menu, so whatever your other tools add to Explorer is still one click away
- Filename search with wildcard patterns (`*.log`, `report_*.xlsx`, etc.)
- Content search streams through files line by line without loading them into RAM
- Multicore engine: The scan parallelizes across every CPU core
- Multiple search terms in a single pass, each independently tracked
- Filters by extension, date modified, and size, plus include/exclude patterns and a case-sensitive toggle
- Tabs: Each tab is a whole independent search or a folder you are browsing; drag to reorder, optionally restored on the next launch
- Search within results: Pipe one search's results into a new tab and drill deeper
- Sort results by name, location, size, or modified date; Ctrl+F filters the list live<br>*(Yo dawg, I heard you liked search...)*
- Results show matched lines with line numbers; open files directly or reveal them in Explorer
- Export to a self-contained HTML report (with its own theme switcher) or CSV
- Six killer themes with live accent colors; UI localized in 10 languages
- Keyboard driven: Explorer's conventions where they exist (Enter opens, F2 renames, Alt+Enter for properties, Shift+F10 for the shell menu), and F1 opens a shortcuts card that lists every gesture as both a list and a visual keyboard
- Run portable, or install for just you or for every user on the PC (`/silent` installs machine-wide for winget/RMM)

## Requirements

- Windows 10 or 11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included in Windows 10 1903+ and Windows 11)

## Download

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerFind/releases/latest/download/KillerFind.exe>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerFind/releases/download/v1.0.1/KillerFind-1.0.1-src.zip>

## Build

Open `KillerFind.sln` in Visual Studio 2022 and build. No external dependencies beyond the NuGet packages in the project file.

`release.ps1` additionally produces a versioned `KillerFind-<version>-src.zip` next to the published EXE, which is the GPL3 corresponding source published with every release.

## License

GPL-3.0 - see [LICENSE](LICENSE).
