using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerFind
{
    // Code-behind for one results pane (FilePane.xaml).
    //
    // The pane owns MARKUP AND NOTHING ELSE. Every handler below is a one-line forward to the
    // window, because the logic is the same whichever pane raised it and duplicating any of it
    // here is exactly what the extraction was for.
    //
    // The forwarders exist because WPF resolves a handler name against the class that declares
    // the XAML. Moving the markup out of MainWindow.xaml therefore moved every Click and
    // MouseDown with it, and the only way back to the window's handlers is through this file.
    // MainWindow's handlers are `internal` rather than `private` for the same reason.
    public partial class FilePane : UserControl
    {
        public FilePane() => InitializeComponent();

        // Resolved on first use, not in the constructor: the pane is built during the window's
        // InitializeComponent, before there is a window to find. Every handler runs long after
        // load, so by the time any of them fires the tree is up.
        private MainWindow? _owner;
        internal MainWindow Owner => _owner ??= (MainWindow)Window.GetWindow(this)!;

        // ── Navigation + address bar ─────────────────────────────
        private void NavBack_Click(object s, RoutedEventArgs e)            => Owner.NavBack_Click(s, e);
        private void NavForward_Click(object s, RoutedEventArgs e)         => Owner.NavForward_Click(s, e);
        private void NavUp_Click(object s, RoutedEventArgs e)              => Owner.NavUp_Click(s, e);
        private void ScopeBar_Click(object s, MouseButtonEventArgs e)      => Owner.ScopeBar_Click(s, e);
        private void AddressBox_KeyDown(object s, KeyEventArgs e)          => Owner.AddressBox_KeyDown(s, e);
        private void AddressBox_LostFocus(object s, RoutedEventArgs e)     => Owner.AddressBox_LostFocus(s, e);

        // ── View mode, sort, view options ────────────────────────
        private void ViewList_Click(object s, RoutedEventArgs e)           => Owner.ViewList_Click(s, e);
        private void ViewIcons_Click(object s, RoutedEventArgs e)          => Owner.ViewIcons_Click(s, e);
        private void ViewDetails_Click(object s, RoutedEventArgs e)        => Owner.ViewDetails_Click(s, e);
        private void SortCombo_Changed(object s, SelectionChangedEventArgs e) => Owner.SortCombo_Changed(s, e);
        private void SortDir_Click(object s, RoutedEventArgs e)            => Owner.SortDir_Click(s, e);
        private void ShowHidden_Click(object s, RoutedEventArgs e)         => Owner.ShowHidden_Click(s, e);
        private void FoldersTop_Click(object s, RoutedEventArgs e)         => Owner.FoldersTop_Click(s, e);
        private void ExpandAll_Click(object s, RoutedEventArgs e)          => Owner.ExpandAll_Click(s, e);
        private void FavouriteStar_Click(object s, RoutedEventArgs e)      => Owner.FavouriteStar_Click(s, e);

        // ── Details-view column headers ──────────────────────────
        private void ColName_Click(object s, RoutedEventArgs e)            => Owner.ColName_Click(s, e);
        private void ColFolder_Click(object s, RoutedEventArgs e)          => Owner.ColFolder_Click(s, e);
        private void ColSize_Click(object s, RoutedEventArgs e)            => Owner.ColSize_Click(s, e);
        private void ColModified_Click(object s, RoutedEventArgs e)        => Owner.ColModified_Click(s, e);

        // ── Pipe + export ────────────────────────────────────────
        private void PipeButton_Click(object s, RoutedEventArgs e)         => Owner.PipeButton_Click(s, e);
        private void PipeTab_Click(object s, RoutedEventArgs e)            => Owner.PipeTab_Click(s, e);
        private void Export_Click(object s, RoutedEventArgs e)             => Owner.Export_Click(s, e);
        private void ExportCsv_Click(object s, RoutedEventArgs e)          => Owner.ExportCsv_Click(s, e);

        // ── Tabs ─────────────────────────────────────────────────
        private void Tab_MouseDown(object s, MouseButtonEventArgs e)       => Owner.Tab_MouseDown(s, e);
        private void Tab_DragDown(object s, MouseButtonEventArgs e)        => Owner.Tab_DragDown(s, e);
        private void Tab_DragMove(object s, MouseEventArgs e)              => Owner.Tab_DragMove(s, e);
        private void Tab_DragUp(object s, MouseButtonEventArgs e)          => Owner.Tab_DragUp(s, e);
        private void CloseTab_Click(object s, RoutedEventArgs e)           => Owner.CloseTab_Click(s, e);

        // ── Results list: rows, gestures, quick filter ───────────
        private void ResultHeader_Click(object s, MouseButtonEventArgs e)     => Owner.ResultHeader_Click(s, e);
        private void ResultHeader_MouseDown(object s, MouseButtonEventArgs e) => Owner.ResultHeader_MouseDown(s, e);
        private void OpenFile_Click(object s, RoutedEventArgs e)            => Owner.OpenFile_Click(s, e);
        private void ShowInExplorer_Click(object s, RoutedEventArgs e)      => Owner.ShowInExplorer_Click(s, e);
        private void ResultFilterBox_TextChanged(object s, TextChangedEventArgs e) => Owner.ResultFilterBox_TextChanged(s, e);
        private void ResultFilterClose_Click(object s, RoutedEventArgs e)   => Owner.ResultFilterClose_Click(s, e);
        private void FilterGrip_MouseDown(object s, MouseButtonEventArgs e) => Owner.FilterGrip_MouseDown(s, e);
        private void FilterGrip_MouseMove(object s, MouseEventArgs e)       => Owner.FilterGrip_MouseMove(s, e);
        private void FilterGrip_MouseUp(object s, MouseButtonEventArgs e)   => Owner.FilterGrip_MouseUp(s, e);

        private void ResultsList_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e)  => Owner.ResultsList_PreviewMouseLeftButtonDown(s, e);
        private void ResultsList_PreviewMouseMove(object s, MouseEventArgs e)                  => Owner.ResultsList_PreviewMouseMove(s, e);
        private void ResultsList_PreviewMouseLeftButtonUp(object s, MouseButtonEventArgs e)    => Owner.ResultsList_PreviewMouseLeftButtonUp(s, e);
        private void ResultsList_PreviewMouseRightButtonDown(object s, MouseButtonEventArgs e) => Owner.ResultsList_PreviewMouseRightButtonDown(s, e);
        private void ResultsList_PreviewMouseWheel(object s, MouseWheelEventArgs e)            => Owner.ResultsList_PreviewMouseWheel(s, e);
        private void ResultsList_ContextMenuOpening(object s, ContextMenuEventArgs e)          => Owner.ResultsList_ContextMenuOpening(s, e);

        // ── Results context menu ─────────────────────────────────
        private void MenuOpen_Click(object s, RoutedEventArgs e)           => Owner.MenuOpen_Click(s, e);
        private void MenuOpenWith_Click(object s, RoutedEventArgs e)       => Owner.MenuOpenWith_Click(s, e);
        private void MenuOpenAdmin_Click(object s, RoutedEventArgs e)      => Owner.MenuOpenAdmin_Click(s, e);
        private void MenuShowInExplorer_Click(object s, RoutedEventArgs e) => Owner.MenuShowInExplorer_Click(s, e);
        private void MenuFavorite_Click(object s, RoutedEventArgs e)       => Owner.MenuFavorite_Click(s, e);
        private void MenuSearchHere_Click(object s, RoutedEventArgs e)     => Owner.MenuSearchHere_Click(s, e);
        private void MenuExcludeFolder_Click(object s, RoutedEventArgs e)  => Owner.MenuExcludeFolder_Click(s, e);
        private void MenuCopyPath_Click(object s, RoutedEventArgs e)       => Owner.MenuCopyPath_Click(s, e);
        private void MenuCopyName_Click(object s, RoutedEventArgs e)       => Owner.MenuCopyName_Click(s, e);
        private void MenuCopyFolder_Click(object s, RoutedEventArgs e)     => Owner.MenuCopyFolder_Click(s, e);
        private void MenuCopyLines_Click(object s, RoutedEventArgs e)      => Owner.MenuCopyLines_Click(s, e);
        private void MenuCopyFile_Click(object s, RoutedEventArgs e)       => Owner.MenuCopyFile_Click(s, e);
        private void MenuCopyHash_Click(object s, RoutedEventArgs e)       => Owner.MenuCopyHash_Click(s, e);
        private void MenuProperties_Click(object s, RoutedEventArgs e)     => Owner.MenuProperties_Click(s, e);
        private void MenuShell_Click(object s, RoutedEventArgs e)          => Owner.MenuShell_Click(s, e);

        // File operations (FileCommands.cs)
        private void MenuCut_Click(object s, RoutedEventArgs e)            => Owner.MenuCut_Click(s, e);
        private void MenuCopy_Click(object s, RoutedEventArgs e)           => Owner.MenuCopy_Click(s, e);
        private void MenuPaste_Click(object s, RoutedEventArgs e)          => Owner.MenuPaste_Click(s, e);
        private void MenuRename_Click(object s, RoutedEventArgs e)         => Owner.MenuRename_Click(s, e);
        private void MenuDelete_Click(object s, RoutedEventArgs e)         => Owner.MenuDelete_Click(s, e);
        private void MenuNewFolder_Click(object s, RoutedEventArgs e)      => Owner.MenuNewFolder_Click(s, e);
    }
}
