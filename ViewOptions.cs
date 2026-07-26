using System.Windows;

namespace KillerFind
{
    // Two listing preferences that belong to the app rather than to a tab: whether hidden and
    // system entries are shown, and whether folders are pinned above files. Partial of MainWindow.
    //
    // Window-wide on purpose. Explorer and Total Commander both treat these as view settings
    // rather than per-location state, and it means they survive the coming dual-pane split
    // untouched - nothing here reads _active except to re-list what is currently on screen.
    public partial class MainWindow
    {
        // Read by ListFolder (Browse.cs) and by the tree's child enumeration (FolderTree.cs),
        // both of which run off the UI thread, so these stay plain fields with no UI coupling.
        internal static bool ShowHidden   { get; private set; }
        internal static bool FoldersOnTop { get; private set; } = true;

        private void InitViewOptions()
        {
            ShowHidden   = Services.ThemeManager.GetSetting("ShowHidden")   == "1";
            FoldersOnTop = Services.ThemeManager.GetSetting("FoldersOnTop") != "0";   // default on
            UpdateViewOptionButtons();
        }

        private void UpdateViewOptionButtons()
        {
            // E7B3 is the "hidden" eye, E890 the open one, so the glyph says what you are
            // currently looking at rather than what the click would do.
            Pane.ShowHiddenBtn.Content = ((char)(ShowHidden ? 0xE890 : 0xE7B3)).ToString();
            Pane.ShowHiddenBtn.Tag     = ShowHidden ? "on" : null;
            Pane.FoldersTopBtn.Tag     = FoldersOnTop ? "on" : null;
        }

        /// <summary>
        /// Shows the details-view location column only when rows can come from different places.
        /// Called wherever a tab's browsing state can change (Browse.cs, Tabs.cs).
        /// </summary>
        internal void UpdateLocationColumn()
        {
            bool browsing = _active != null && _active.IsBrowsing;

            ResultsViewState.Current.LocationWidth =
                browsing ? new System.Windows.GridLength(0) : ResultsViewState.SearchLocationWidth;

            UpdateBrowseChrome(browsing);
        }

        /// <summary>
        /// The toolbar bits that only mean something over search results. Browsing hides them
        /// rather than leaving controls on screen that do nothing useful where you are standing.
        /// </summary>
        private void UpdateBrowseChrome(bool browsing)
        {
            // Pipe opens a NEW TAB scoped to the listed files. Over a folder listing the funnel
            // reads as "filter these rows", so the tab it opens comes as a surprise; over search
            // results, which is what it was built for, it reads correctly.
            Pane.PipeBtn.Visibility = browsing ? Visibility.Collapsed : Visibility.Visible;

            // "as found" is the ENGINE'S discovery order, which only exists because a search
            // streams hits in as it walks. A folder is enumerated in one pass, so there is no
            // discovery order to show - the entry goes away and name takes over.
            Pane.SortFoundItem.Visibility = browsing ? Visibility.Collapsed : Visibility.Visible;

            if (browsing && _active != null && _active.SortIndex == 0)
            {
                _active.SortIndex = 1;               // name

                // Programmed, not user-driven: suppress SortCombo_Changed and sort by hand, so
                // this works the same whether it runs during a tab switch or a navigation.
                bool wasSyncing = _syncingSort;      // Results.cs
                _syncingSort = true;
                Pane.SortCombo.SelectedIndex = 1;
                _syncingSort = wasSyncing;

                ApplySort(_active);                  // Results.cs
            }
        }

        internal void ShowHidden_Click(object sender, RoutedEventArgs e)
        {
            ShowHidden = !ShowHidden;
            Services.ThemeManager.SetSetting("ShowHidden", ShowHidden ? "1" : "0");
            UpdateViewOptionButtons();

            // Both the listing and the tree were built with the old filter, so both are stale.
            // The tree refreshes IN PLACE (FolderTree.cs) rather than rebuilding from its drive
            // roots: rebuilding threw away every expansion, so the sidebar collapsed and
            // reflowed on a toggle that has nothing to do with what is open.
            _ = RefreshTreeAsync();                 // FolderTree.cs
            if (_active.IsBrowsing && !string.IsNullOrEmpty(_active.CurrentFolder))
                _ = NavigateTo(_active.CurrentFolder!, record: false);   // Browse.cs
        }

        internal void FoldersTop_Click(object sender, RoutedEventArgs e)
        {
            FoldersOnTop = !FoldersOnTop;
            Services.ThemeManager.SetSetting("FoldersOnTop", FoldersOnTop ? "1" : "0");
            UpdateViewOptionButtons();

            // Sort only - no re-listing needed, and every tab's view has to be reprogrammed or
            // the background ones would keep the old grouping until they were next touched.
            foreach (var t in _tabs) ApplySort(t);   // Results.cs
        }
    }
}
