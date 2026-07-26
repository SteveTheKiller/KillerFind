# AvalonEdit, vendored

Source: https://github.com/icsharpcode/AvalonEdit, tag **v6.3.1** (released 2025-04-13, and still
the newest release upstream has cut). No security advisories are published against this project.
License: MIT. The upstream LICENSE and ChangeLog.md sit beside this file.
Taken: 2026-07-26. Only the `ICSharpCode.AvalonEdit` project folder came across; the samples,
tests, documentation and build tooling in that repo did not.

## Why the source and not a package

KillerFind ships as one portable exe with nothing loose beside it. A referenced assembly would
mean either a DLL on disk or a weaver embedding it into the exe, and the source compiled straight
in keeps the single file while leaving every line the exe contains readable in this repo. Same
call that was made for PdfSharpCore in KillerPDF.

## Local modifications

Keep this list current. An upgrade is a fresh extract of the new tag with these applied again.

1. `themes/generic.xaml`: the six `/ICSharpCode.AvalonEdit;component/...` URIs are now
   `/KillerFind;component/third_party/AvalonEdit/...`. The source is compiled into KillerFind.exe,
   so at runtime there is no ICSharpCode.AvalonEdit assembly for the original URIs to name.

## How it is wired into the build

`KillerFind.csproj` removes `third_party\**` from every default glob and then adds it back
deliberately, because the SDK would otherwise sweep the whole tree into the compile:

- **Compile**: every `.cs` except `Properties\AssemblyInfo.cs`, whose assembly attributes would
  collide with KillerFind's own.
- **Page**: every `.xaml`. `themes\generic.xaml` carries a `Link` back to `themes\generic.xaml`
  so WPF finds the default styles where it looks for them; KillerFind's AssemblyInfo already
  declares `ThemeInfo(..., ResourceDictionaryLocation.SourceAssembly)`.
- **EmbeddedResource**: `Highlighting\Resources\*` with an explicit `LogicalName` of
  `ICSharpCode.AvalonEdit.Highlighting.Resources.<file>`. Resources.cs resolves the built-in
  highlightings by that exact string (`typeof(Resources).FullName + "."`), and the name MSBuild
  would infer here starts with `KillerFind.third_party` instead, so every built-in definition
  would come back null.
- **Resource**: `Search\next.png`, `Search\prev.png` and `themes\RightArrow.cur`, all referenced
  from the XAML.

Nothing here is suppressed. This tree builds warning-clean and message-clean under KillerFind's
own settings, nullable included, and every change made to get there is listed under Local
modifications. An upgrade is therefore a merge rather than a re-extract, which is the deliberate
trade: the vendored copy is held to the same bar as the rest of the repo.

## Upgrading

Extract the new tag's `ICSharpCode.AvalonEdit` folder over this one, reapply the modifications
above, then check whether upstream added files under `Highlighting\Resources` or new `.xaml`
(both are wildcarded in the csproj, so they come in on their own) and whether anything new needs
a `Resource` entry.
