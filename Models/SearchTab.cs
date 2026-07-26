using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace KillerFind.Models
{
    // One search tab: a complete, independent search - folder + terms + filters +
    // include/exclude + results + run state. The left panel and results pane always
    // show the ACTIVE tab; switching tabs swaps every ItemsSource/field over to the
    // incoming tab (see MainWindow ApplyTab/CaptureTab).
    public class SearchTab : INotifyPropertyChanged
    {
        public ObservableCollection<TermGroup>    Groups  { get; } = [];
        public ObservableCollection<SearchFilter> Filters { get; } = [];
        public ObservableCollection<SearchResult> Results { get; } = [];

        // Each tab owns its engine so searches on different tabs never share state.
        public SearchEngine Engine { get; } = new();
        public CancellationTokenSource? Cts;
        public bool IsSearching;

        // ── Browsing (Browse.cs) ─────────────────────────────────
        // A tab is either showing a folder's contents or a search's results, in the same
        // Results collection. IsBrowsing says which, so the sort can put folders first and the
        // nav buttons know whether they mean anything.
        public bool   IsBrowsing;
        public string CurrentFolder = string.Empty;

        // Back / forward, browser-style: a list of visited folders plus a cursor into it, rather
        // than two stacks, so Forward survives going Back several steps.
        public List<string> History      = [];
        public int          HistoryIndex = -1;

        // Search config captured from the left panel when switching away.
        public string RootPath        = string.Empty;
        public string IncludePatterns = "*.*";
        public string ExcludePatterns = string.Empty;
        public bool   CaseSensitive;

        // Last-known footer/status text so a tab switch restores what this search showed.
        public string StatusMessage = string.Empty;
        public string ScannedLabel  = string.Empty;
        public string StatsLabel    = string.Empty;

        // Raw pieces behind the rendered lines above: resource key + args instead of
        // final text, so a live language switch can re-render EVERY tab's status
        // (RelocalizeDynamicUi). Null/-1 = nothing stored (transient text stays as-is).
        public string?   StatusKey;
        public object[]? StatusArgs;
        public long      ScannedCount = -1;
        public object[]? PipeArgs;     // {count, source title, query} for Str_Pipe_Scope
        // Human-readable summary of what this tab last searched for, shown in the
        // results header so old tabs stay self-explanatory.
        public string QueryLabel    = string.Empty;

        // Results sort (mirrors the HTML report): 0 = as found, 1 = name,
        // 2 = location, 3 = size, 4 = modified.
        public int  SortIndex;
        public bool SortAsc = true;

        // Quick filter (Ctrl+F) narrowing the visible results by name/path.
        public string FilterText = string.Empty;

        // Piped scope: when set, this tab searches THIS file list (a snapshot of another
        // tab's results) instead of walking RootPath. Picking a folder clears it.
        public List<string>? PipeFiles;
        public string PipeLabel = string.Empty;   // breadcrumb shown in the location row

        public SearchTab(string title) { _title = title; }

        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; Notify(); }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Notify(); }
        }

        // Rightmost tab in the strip. The tab's 1px right border is a divider BETWEEN tabs,
        // so the last one has to drop it - there it lands on the strip's edge and reads as a
        // stray rule. UpdateTabBar sets this on every add, close and drag-reorder.
        private bool _isLast;
        public bool IsLast
        {
            get => _isLast;
            set { _isLast = value; Notify(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
