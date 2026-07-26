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
    }
}
