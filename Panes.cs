using System.Collections.ObjectModel;
using KillerFind.Models;

namespace KillerFind
{
    // Which pane the window's commands act on. Partial of MainWindow.
    //
    // There is one pane today. When the dual-pane split lands, the focused pane moves with the
    // click and every command below keeps working untouched - which is the whole reason the
    // named controls are reached through this one property instead of being referenced
    // directly at 133 call sites. Getting that indirection in first means the second pane is a
    // layout change rather than another sweep through every file.
    public partial class MainWindow
    {
        private FilePane? _focus;

        /// <summary>
        /// The pane every command acts on. Resolved on first use rather than in the ctor: the
        /// panes are built by InitializeComponent, and some initialisation runs before that
        /// finishes.
        /// </summary>
        internal FilePane Pane => _focus ??= LeftPane;

        /// <summary>
        /// Point every command at <paramref name="pane"/>. Raised by a click anywhere inside a
        /// pane (FilePane's ctor).
        /// </summary>
        internal void FocusPane(FilePane pane)
        {
            if (_focus == pane) return;   // always true while there is one pane
            _focus = pane;

            // The window chrome - search panel, footer line, nav buttons - shows the focused
            // pane's tab, so moving focus has to re-point it at that pane's active tab.
            if (pane.Active != null) ActivateTab(pane.Active);
        }

        // Tabs belong to a PANE, not to the window (FilePane.Tabs / FilePane.Active). These two
        // keep the old field names so the ~100 call sites across Tabs.cs, Session.cs, Results.cs
        // and the rest read exactly as they did - they now just resolve against whichever pane
        // has focus instead of against one window-wide collection.
        private ObservableCollection<SearchTab> _tabs => Pane.Tabs;

        private SearchTab _active
        {
            get => Pane.Active;
            set => Pane.Active = value;
        }
    }
}
