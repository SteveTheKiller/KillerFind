using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerFind.Models;

namespace KillerFind
{
    // The results context menu. Partial of MainWindow.
    //
    // One menu serves all three views, declared once as a resource, and none of its items carry a
    // per-item Tag binding. Instead the right-click records what was under the pointer and every
    // command asks FilesForCommand (ResultsInteraction.cs) what to act on. That is what lets a
    // command work on a whole selection - "copy 12 paths" - which a Tag binding cannot express,
    // and it keeps one menu definition instead of three that drift apart.
    public partial class MainWindow
    {
        private SearchResult? _menuSeed;

        internal void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemUnder(e.OriginalSource as System.Windows.DependencyObject);
            _menuSeed = item?.DataContext as SearchResult;

            // Right-clicking outside the selection moves the selection there first, so the menu
            // always acts on what you are pointing at. Right-clicking inside it leaves the
            // selection alone, so a multi-file command still sees all of them.
            if (_menuSeed != null && !Pane.ResultsList.SelectedItems.OfType<SearchResult>().Contains(_menuSeed))
            {
                Pane.ResultsList.SelectedItems.Clear();
                Pane.ResultsList.SelectedItems.Add(_menuSeed);
            }
        }

        // The seed again, resolved as the menu opens rather than only on the press. Mouse
        // .DirectlyOver at this instant is the element the menu is opening for, which covers the
        // cases the press handler cannot see - a menu raised from the keyboard, or a press that
        // landed on something the visual walk did not resolve to a container.
        internal void ResultsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var seed = ItemUnder(Mouse.DirectlyOver as System.Windows.DependencyObject)?.DataContext as SearchResult
                    ?? ItemUnder(e.OriginalSource as System.Windows.DependencyObject)?.DataContext as SearchResult;

            if (seed != null) _menuSeed = seed;

            // Same rule as the press handler: pointing outside the selection moves it here,
            // pointing inside it leaves a multi-file selection intact.
            if (_menuSeed != null && !Pane.ResultsList.SelectedItems.OfType<SearchResult>().Contains(_menuSeed))
            {
                Pane.ResultsList.SelectedItems.Clear();
                Pane.ResultsList.SelectedItems.Add(_menuSeed);
            }

            // One item covers both directions, so its header has to be decided per opening.
            //
            // Found by Tag rather than x:Name: the menu is declared inside ListBox.Resources, and
            // XAML does not generate fields for elements inside a resource, so an x:Name there
            // compiles to nothing the code-behind can see. Cached after the first lookup.
            _favMenuItem ??= Pane.ResultsList.ContextMenu?.Items.OfType<MenuItem>()
                                        .FirstOrDefault(m => (m.Tag as string) == "fav");

            if (_favMenuItem != null)
                _favMenuItem.Header = Loc(IsBookmarked(MenuFolder())
                    ? "Str_Menu_RemoveFavorite"
                    : "Str_Menu_AddFavorite");
        }

        private MenuItem? _favMenuItem;

        // Favourites are folders, so this acts on the seed itself when it is one and on the
        // parent when it is a file - which is what MenuFolder already means (Bookmarks.cs).
        internal void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            string? folder = MenuFolder();
            if (folder == null) return;

            if (IsBookmarked(folder)) RemoveBookmark(folder);
            else                      AddBookmark(folder);
        }

        private List<string> MenuFiles() => FilesForCommand(_menuSeed);
        private string?      MenuFile()  => MenuFiles().FirstOrDefault();

        // Browsing put folders in this list, and every command below used to guard on
        // File.Exists - which is false for a directory, so right-clicking a folder ran the
        // command and silently did nothing. Anything that works on either kind asks this
        // instead; the few that are genuinely file-only say so out loud rather than no-op.
        private static bool Exists(string? p)
            => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p));

        // Clipboard writes fail if another process is holding the clipboard open. Nothing we can
        // do about that, but silently doing nothing is worse than saying so.
        private void PutOnClipboard(string text, string statusKey, params object[] args)
        {
            try
            {
                Clipboard.SetText(text);
                SetTabStatusKey(_active, statusKey, args);
            }
            catch { SetTabStatusKey(_active, "Str_Status_ClipboardBusy"); }
        }

        // ── Open ─────────────────────────────────────────────────
        internal void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            string? p = MenuFile();
            if (!Exists(p)) return;

            // A folder opens the way it does everywhere else in the app: you go into it.
            if (Directory.Exists(p) && _active.IsBrowsing)
            {
                _ = NavigateTo(p!);   // Browse.cs
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p!) { UseShellExecute = true });
        }

        internal void MenuOpenWith_Click(object sender, RoutedEventArgs e)
        {
            // OpenAs_RunDLL takes the rest of the command line as the path - no quotes, even
            // when the path has spaces in it. Genuinely file-only: there is no "open a folder
            // with" chooser.
            string? p = MenuFile();
            if (p == null || !File.Exists(p)) { SetTabStatusKey(_active, "Str_Status_FileOnly"); return; }
            System.Diagnostics.Process.Start("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {p}");
        }

        internal void MenuOpenAdmin_Click(object sender, RoutedEventArgs e)
        {
            string? p = MenuFile();
            if (p == null || !File.Exists(p)) { SetTabStatusKey(_active, "Str_Status_FileOnly"); return; }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p)
                {
                    UseShellExecute = true,
                    Verb            = "runas",
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The user declined the UAC prompt. That is an answer, not a failure.
                SetTabStatusKey(_active, "Str_Status_ElevationDeclined");
            }
        }

        // /select works for a folder too - it opens the parent with the folder highlighted,
        // which is what "show me where this is" means for either kind.
        internal void MenuShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            string? p = MenuFile();
            if (Exists(p)) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{p}\"");
        }

        // The real Windows properties dialog, not a copy of it: Security, Details, Previous
        // Versions and whatever tabs the installed shell extensions add. Works on folders too.
        //
        // Deferred to after the menu has finished closing. Both shell commands were being called
        // straight out of the Click handler, while the WPF ContextMenu was still dismissing and
        // still holding mouse capture - which is exactly the state the shell cannot open a dialog
        // or track a popup in, so both silently did nothing.
        internal void MenuProperties_Click(object sender, RoutedEventArgs e)
        {
            string? p = MenuFile();
            if (!Exists(p)) return;

            AfterMenuCloses(() =>
            {
                if (!Services.ShellContextMenu.ShowProperties(p!))
                    SetTabStatusKey(_active, "Str_Status_ShellFailed");
            });
        }

        /// <summary>
        /// Runs an action once the context menu is fully closed and has given up capture.
        /// Background priority is late enough that the menu's own teardown has run.
        /// </summary>
        private void AfterMenuCloses(Action work)
            => Dispatcher.BeginInvoke(work, System.Windows.Threading.DispatcherPriority.Background);

        // ── Copy ─────────────────────────────────────────────────
        // All of these act on the whole selection, one entry per line, because pasting twelve
        // paths into a ticket is the actual use.
        internal void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            var files = MenuFiles();
            if (files.Count == 0) return;
            PutOnClipboard(string.Join(Environment.NewLine, files),
                           "Str_Status_Copied", files.Count.ToString("N0"));
        }

        internal void MenuCopyName_Click(object sender, RoutedEventArgs e)
        {
            var names = MenuFiles().Select(Path.GetFileName).ToList();
            if (names.Count == 0) return;
            PutOnClipboard(string.Join(Environment.NewLine, names),
                           "Str_Status_Copied", names.Count.ToString("N0"));
        }

        internal void MenuCopyFolder_Click(object sender, RoutedEventArgs e)
        {
            // Distinct: several results from one folder should not paste that folder five times.
            var folders = MenuFiles()
                .Select(f => Path.GetDirectoryName(f) ?? string.Empty)
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (folders.Count == 0) return;
            PutOnClipboard(string.Join(Environment.NewLine, folders),
                           "Str_Status_Copied", folders.Count.ToString("N0"));
        }

        // Content hits only. A filename match has no lines behind it, so this is empty for one.
        internal void MenuCopyLines_Click(object sender, RoutedEventArgs e)
        {
            var results = Pane.ResultsList.SelectedItems.OfType<SearchResult>().ToList();
            if (results.Count == 0 && _menuSeed != null) results.Add(_menuSeed);

            var sb = new StringBuilder();
            int lines = 0;

            foreach (var r in results)
            {
                var content = r.Matches.Where(m => m.Lines.Count > 0).ToList();
                if (content.Count == 0) continue;

                sb.AppendLine(r.FilePath);
                foreach (var m in content)
                    foreach (var l in m.Lines)
                    {
                        sb.AppendLine($"  {l.LineNumber}: {l.LineText}");
                        lines++;
                    }
                sb.AppendLine();
            }

            if (lines == 0) { SetTabStatusKey(_active, "Str_Status_NoLines"); return; }
            PutOnClipboard(sb.ToString(), "Str_Status_CopiedLines", lines.ToString("N0"));
        }

        // Puts the files themselves on the clipboard, so Ctrl+V in Explorer pastes real copies.
        // CF_HDROP plus the Preferred DropEffect blob: without that blob Explorer picks its own
        // idea of copy versus move, and a move would take the original away.
        internal void MenuCopyFile_Click(object sender, RoutedEventArgs e)
        {
            // Folders included: CF_HDROP carries either, and Explorer pastes a folder copy
            // exactly as happily as a file copy.
            var files = MenuFiles().Where(Exists).ToArray();
            if (files.Length == 0) return;

            try
            {
                var list = new System.Collections.Specialized.StringCollection();
                foreach (var f in files) list.Add(f);

                var data = new DataObject();
                data.SetFileDropList(list);

                var effect = new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy));
                data.SetData("Preferred DropEffect", effect);

                Clipboard.SetDataObject(data, true);
                SetTabStatusKey(_active, "Str_Status_CopiedFiles", files.Length.ToString("N0"));
            }
            catch { SetTabStatusKey(_active, "Str_Status_ClipboardBusy"); }
        }

        // ── Hash ─────────────────────────────────────────────────
        // Off the UI thread: this reads the whole file, and results can be gigabytes.
        internal async void MenuCopyHash_Click(object sender, RoutedEventArgs e)
        {
            string? p = MenuFile();
            if (p == null || !File.Exists(p)) { SetTabStatusKey(_active, "Str_Status_FileOnly"); return; }

            SetTabStatusKey(_active, "Str_Status_Hashing", Path.GetFileName(p));
            string? hash = await Task.Run(() =>
            {
                try
                {
                    using var sha = SHA256.Create();
                    using var fs  = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
                }
                catch { return null; }
            });

            if (hash == null) { SetTabStatusKey(_active, "Str_Status_HashFailed"); return; }
            PutOnClipboard(hash, "Str_Status_HashCopied", Path.GetFileName(p));
        }

        // ── Turning a result into the next search ────────────────
        // Right-clicking a FOLDER means that folder, not the one it sits in. Right-clicking a
        // file means the folder holding it. Getting this backwards for folders would send you up
        // a level, which is the opposite of what the command says.
        private string? MenuFolder()
        {
            string? p = MenuFile();
            if (p == null) return null;
            if (Directory.Exists(p)) return p;
            string? parent = Path.GetDirectoryName(p);
            return Directory.Exists(parent) ? parent : null;
        }

        internal void MenuSearchHere_Click(object sender, RoutedEventArgs e)
        {
            string? folder = MenuFolder();
            if (folder == null) return;

            ScopeToFolder(folder);   // ResultsInteraction.cs - same path a dropped folder takes
        }

        // Appends the folder NAME rather than its full path, because that is what the exclude
        // syntax matches on: a bare name kills that folder anywhere in the tree, which is the
        // point of excluding bin or node_modules (SearchEngine.IsExcluded).
        internal void MenuExcludeFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = MenuFolder();
            if (string.IsNullOrEmpty(folder)) return;

            string name = Path.GetFileName(folder!.TrimEnd(Path.DirectorySeparatorChar));
            if (name.Length == 0) return;

            string current = ExcludePatternsBox.Text.Trim();
            var parts = current.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (parts.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
            {
                SetTabStatusKey(_active, "Str_Status_AlreadyExcluded", name);
                return;
            }

            parts.Add(name);
            ExcludePatternsBox.Text  = string.Join(";", parts);
            _active.ExcludePatterns  = ExcludePatternsBox.Text;
            SetTabStatusKey(_active, "Str_Status_Excluded", name);
        }

        // ── The real Windows menu ────────────────────────────────
        internal void MenuShell_Click(object sender, RoutedEventArgs e)
        {
            var files = MenuFiles().Where(Exists).ToArray();
            if (files.Length == 0) return;

            // TrackPopupMenuEx needs the mouse, and the WPF menu still has it at Click time.
            AfterMenuCloses(() =>
            {
                if (!Services.ShellContextMenu.Show(this, files))
                    SetTabStatusKey(_active, "Str_Status_ShellFailed");
            });
        }
    }
}
