using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KillerFind.Models;

namespace KillerFind
{
    // Browsing a folder. Partial of MainWindow.
    //
    // A browsed listing goes into the SAME collection a search fills (tab.Results), as
    // SearchResult entries with IsDirectory set on the folders. That is the point of the whole
    // design rather than a convenience: it means the three views, the sort, the quick filter,
    // marquee selection, drag and drop and every context-menu command work on browsed entries
    // with no second implementation, and it leaves room for a search to drop its hits into the
    // folder you are already looking at instead of a separate list.
    //
    // Listing runs off the UI thread and lands in one assignment. A folder with 50k entries is
    // not rare (a node_modules, a mail store, a photo dump), and enumerating that on the
    // dispatcher would freeze the window the same way unbounded result batches used to.
    public partial class MainWindow
    {
        private CancellationTokenSource? _listCts;

        /// <summary>
        /// Show <paramref name="folder"/>. Records history unless this IS a history move, which
        /// is what stops Back from pushing the place you just came from and trapping you.
        /// </summary>
        private async Task NavigateTo(string folder, bool record = true)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;

            try { folder = Path.GetFullPath(folder); }
            catch { SetTabStatusKey(_active, "Str_Status_BadPath", folder); return; }

            if (!Directory.Exists(folder))
            {
                SetTabStatusKey(_active, "Str_Status_BadPath", folder);
                return;
            }

            var tab = _active;

            if (record && !string.Equals(tab.CurrentFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                // A new move truncates anything forward of here, the way every browser does it.
                if (tab.HistoryIndex < tab.History.Count - 1)
                    tab.History.RemoveRange(tab.HistoryIndex + 1, tab.History.Count - tab.HistoryIndex - 1);
                tab.History.Add(folder);
                tab.HistoryIndex = tab.History.Count - 1;
            }

            tab.CurrentFolder = folder;
            tab.IsBrowsing    = true;
            tab.Title         = FolderTitle(folder);

            // Cancel a listing still running for the folder we just left, or a slow network
            // share would land its results on top of the folder you moved to.
            _listCts?.Cancel();
            _listCts = new CancellationTokenSource();
            var ct = _listCts.Token;

            RootPathBox.Text    = folder;
            ScopePathLabel.Text = folder;
            UpdateNavButtons();
            SetTabStatusKey(tab, "Str_Status_Listing", folder);

            List<SearchResult> entries;
            try { entries = await Task.Run(() => ListFolder(folder, ct), ct); }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested || tab != _active) return;

            tab.Results.Clear();
            foreach (var e in entries) tab.Results.Add(e);

            ApplySort(tab);       // Results.cs - folders-first is added there while browsing
            ApplyFilter(tab);

            // Watch AFTER the listing lands, so the first events cannot arrive against a
            // collection that is still being filled (BrowseWatcher.cs).
            StartWatching(folder);

            ResultsHeader.Text = string.Format(Loc("Str_Lbl_ResultsCount"), tab.Results.Count);
            SetTabStatusKey(tab, "Str_Status_Listed", entries.Count.ToString("N0"));
            UpdateTabBar();

            UpdateFavouriteStar();   // Bookmarks.cs - a new folder changes what the star means
            UpdateLocationColumn();  // ViewOptions.cs - browsing needs no per-row folder

            // Point the tree at where we landed, whichever route got us here - the tree's own
            // selection handler is what called this in the first place when it was the route,
            // and RevealInTree guards that case (FolderTree.cs). Not awaited: expanding the
            // chain can touch a slow drive and the listing is already on screen.
            _ = RevealInTree(folder);
        }

        // Everything in one pass, each entry stat'd once. Enumerating the FileSystemInfo rather
        // than the path string means Windows hands back size and timestamp with the entry, so the
        // sort keys cost nothing extra - the same trick worth doing in the search engine.
        private static List<SearchResult> ListFolder(string folder, CancellationToken ct)
        {
            var list = new List<SearchResult>();
            int seq = 0;

            try
            {
                // ONE interleaved pass, not directories-then-files.
                //
                // Enumerating them separately baked folders-first into the listing itself, which
                // left the folders-on-top toggle with nothing to do: under the default "as found"
                // sort the view carries no SortDescription at all, so switching the option off
                // just fell back to the underlying collection order - which was already grouped.
                // The toggle looked dead because the grouping was never the sort's doing.
                //
                // Discovery order is genuinely mixed now (NTFS hands these back alphabetically),
                // so "as found" means what it says and folders-first is purely a view concern.
                foreach (var e in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if (ct.IsCancellationRequested) return list;

                    // One Attributes read, reused: each call is a stat on some providers.
                    FileAttributes a;
                    try { a = e.Attributes; } catch { continue; }   // vanished mid-enumeration

                    if (!ShowHidden && (a & FileAttributes.Hidden) != 0) continue;   // ViewOptions.cs

                    bool isDir = (a & FileAttributes.Directory) != 0;
                    list.Add(new SearchResult
                    {
                        FilePath    = e.FullName,
                        FileName    = e.Name,
                        Directory   = folder,
                        IsDirectory = isDir,
                        SizeBytes   = isDir ? 0 : SafeLength((FileInfo)e),
                        Modified    = SafeWriteTime(e),
                        Seq         = seq++,
                    });
                }
            }
            catch (UnauthorizedAccessException) { /* listed what we could see */ }
            catch (IOException) { }

            return list;
        }

        // A file can vanish or refuse a stat between being enumerated and being read. Neither is
        // worth losing the whole listing over.
        private static DateTime SafeWriteTime(FileSystemInfo i)
        {
            try { return i.LastWriteTime; } catch { return default; }
        }

        private static long SafeLength(FileInfo f)
        {
            try { return f.Length; } catch { return 0; }
        }

        private static string FolderTitle(string folder)
        {
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            return name.Length > 0 ? name : folder;   // a drive root has no name component
        }

        // ── History ──────────────────────────────────────────────
        private async void NavBack_Click(object sender, RoutedEventArgs e)
        {
            var t = _active;
            if (t.HistoryIndex <= 0) return;
            t.HistoryIndex--;
            await NavigateTo(t.History[t.HistoryIndex], record: false);
        }

        private async void NavForward_Click(object sender, RoutedEventArgs e)
        {
            var t = _active;
            if (t.HistoryIndex >= t.History.Count - 1) return;
            t.HistoryIndex++;
            await NavigateTo(t.History[t.HistoryIndex], record: false);
        }

        private async void NavUp_Click(object sender, RoutedEventArgs e)
        {
            string? parent = ParentOf(_active.CurrentFolder);
            if (parent != null) await NavigateTo(parent);
        }

        // Null at a drive root, which is where Up should stop until the This PC page exists.
        private static string? ParentOf(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try
            {
                var parent = System.IO.Directory.GetParent(folder.TrimEnd(Path.DirectorySeparatorChar));
                return parent?.FullName;
            }
            catch { return null; }
        }

        private void UpdateNavButtons()
        {
            var t = _active;
            NavBackBtn.IsEnabled    = t.HistoryIndex > 0;
            NavForwardBtn.IsEnabled = t.HistoryIndex < t.History.Count - 1;
            NavUpBtn.IsEnabled      = ParentOf(t.CurrentFolder) != null;
        }

        /// <summary>Enter a folder, or open a file. What a double-click means in browse mode.</summary>
        internal async void ActivateEntry(SearchResult r)
        {
            if (r.IsDirectory) { await NavigateTo(r.FilePath); return; }

            if (File.Exists(r.FilePath))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(r.FilePath) { UseShellExecute = true });
        }
    }
}
