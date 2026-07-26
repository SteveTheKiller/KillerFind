using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace KillerFind
{
    // The search panel's open/closed state. Partial of MainWindow.
    //
    // Search used to BE the app, so its config panel owned the left third of the window whether
    // you were searching or not. Now that browsing is the primary mode the panel is optional:
    // closed by default, opened from the rail chevron or with Ctrl+Shift+S, and the choice is
    // remembered. It sits on the RIGHT edge now - the left is the folder tree's, which is where
    // a file manager puts it.
    //
    // The toggle button deliberately lives on the rail rather than on the panel: a panel
    // collapsed to zero width would take its own handle with it and there would be no way back.
    // The rail is on the left even though this panel is on the right, because one rail for two
    // panels beats a second strip of chrome carrying a single button.
    public partial class MainWindow
    {
        private bool _searchOpen;

        // The width the panel opens to. There is no column splitter yet, so this is fixed; when
        // one arrives, this is the value it should persist.
        private const double SearchPanelWidth = 265;

        private void InitSearchPanel()
        {
            _searchOpen = Services.ThemeManager.GetSetting("SearchPanelOpen") == "1";
            ApplySearchPanel(animate: false);   // startup should not slide
        }

        private void SearchPanel_Click(object sender, RoutedEventArgs e) => ToggleSearchPanel();

        internal void ToggleSearchPanel()
        {
            _searchOpen = !_searchOpen;
            ApplySearchPanel(animate: true);
            Services.ThemeManager.SetSetting("SearchPanelOpen", _searchOpen ? "1" : "0");

            // Opening it is nearly always a prelude to typing a term, so put the caret there.
            if (_searchOpen) FocusFirstTerm();
        }

        // The slide itself is shared with the folder tree (PanelSlide.cs) so the two edges of
        // the window move identically.
        private void ApplySearchPanel(bool animate)
        {
            // The glyph stays a magnifier in both states and the accent carries the state
            // instead (RailButton's Tag="on" trigger). Swapping it for a chevron on open read as
            // a different button rather than the same one lit up.
            SearchPanelBtn.Tag = _searchOpen ? "on" : null;

            // No gap: the panel butts straight against the results pane. The pane's own 8px
            // right margin goes with it, or the two would still add up to a visible trench -
            // that margin exists to hold the pane off the WINDOW edge, and while the panel is
            // open there is no window edge there to hold it off.
            Pane.ResultsPane.Margin    = new Thickness(0, 0, _searchOpen ? 0 : 8, 0);
            Pane.TabFadeGhost.Margin   = Pane.ResultsPane.Margin;

            // The tab strip has to travel with the pane it sits on. It was left behind here, so
            // opening the search panel moved the pane 8px right and left the strip where it was -
            // the tabs then hung off the pane's right edge instead of lining up with it.
            Pane.TabBar.Margin         = new Thickness(0, 6, _searchOpen ? 0 : 8, 0);

            // Right-hand panel, so its contents stay pinned to the RIGHT edge during the tween.
            SlideColumn(SearchCol, SearchPanel, _searchOpen,
                        SearchPanelWidth, minOpen: 200, maxOpen: 380,
                        freezeAlign: HorizontalAlignment.Right, animate: animate);
        }

        /// <summary>
        /// Explorer's Ctrl+E: put the caret in the search box. Opens the panel first if it is
        /// shut - and stops there, because ToggleSearchPanel already focuses on the way open and
        /// focusing twice would fight the slide.
        /// </summary>
        internal void FocusSearchTerms()
        {
            if (!_searchOpen) { ToggleSearchPanel(); return; }
            FocusFirstTerm();
        }

        // The first term box in the first group, if the panel has one built yet.
        private void FocusFirstTerm()
        {
            Dispatcher.InvokeAsync(() =>
            {
                TermsList.UpdateLayout();
                var box = FindDescendant<System.Windows.Controls.TextBox>(TermsList);
                box?.Focus();
                box?.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
