using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace KillerFind
{
    // The folder tree panel's open/closed state and width. Partial of MainWindow.
    //
    // Open by default, unlike the search panel: KillerFind is a shell now, and a file manager
    // that starts with no way to get anywhere is just a list. Both the open/shut choice and the
    // dragged width are remembered.
    public partial class MainWindow
    {
        private bool _treeOpen = true;

        // Where the panel opens to. Seeded from the last drag, so a resize survives collapsing
        // the sidebar and restarting the app.
        private double _treeWidth = TreeWidthDefault;

        private const double TreeWidthDefault = 240;
        private const double TreeWidthMin     = 160;
        private const double TreeWidthMax     = 420;

        private void InitTreePanel()
        {
            // Defaults to open, so only an explicit "0" closes it. A first run with no setting
            // stored should show the tree.
            _treeOpen = Services.ThemeManager.GetSetting("TreePanelOpen") != "0";

            // Invariant culture on the round trip: a saved "240.5" must not become unparseable
            // for anyone whose decimal separator is a comma.
            string saved = Services.ThemeManager.GetSetting("TreePanelWidth") ?? string.Empty;
            if (double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
                _treeWidth = Clamp(w);

            ApplyTreePanel(animate: false);   // startup should not slide
        }

        private static double Clamp(double w)
            => Math.Max(TreeWidthMin, Math.Min(TreeWidthMax, w));

        private void TreePanel_Click(object sender, RoutedEventArgs e) => ToggleTreePanel();

        internal void ToggleTreePanel()
        {
            _treeOpen = !_treeOpen;
            ApplyTreePanel(animate: true);
            Services.ThemeManager.SetSetting("TreePanelOpen", _treeOpen ? "1" : "0");

            // Reopening should land on wherever the active tab already is rather than on
            // whatever was selected when it was closed.
            if (_treeOpen && _active.IsBrowsing && !string.IsNullOrEmpty(_active.CurrentFolder))
                _ = RevealInTree(_active.CurrentFolder!);
        }

        // ── Resize ───────────────────────────────────────────────
        // Driven off ActualWidth rather than an accumulated total, so the panel cannot drift
        // away from the pointer when a drag runs into the clamp and the mouse keeps going.
        private void TreeResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_treeOpen) return;

            double next = Clamp(TreeCol.ActualWidth + e.HorizontalChange);
            if (Math.Abs(next - _treeWidth) < 0.5) return;

            _treeWidth = next;

            // Straight to the column, no animation: a tween would lag the pointer. MinWidth has
            // to move with it, or a Grid column pinned at a MinWidth ignores the new Width.
            TreeCol.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
            TreeCol.MinWidth = TreeWidthMin;
            TreeCol.MaxWidth = TreeWidthMax;
            TreeCol.Width    = new GridLength(_treeWidth);
        }

        private void TreeResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
            => Services.ThemeManager.SetSetting(
                   "TreePanelWidth", _treeWidth.ToString("0.##", CultureInfo.InvariantCulture));

        // The slide itself is shared with the search panel (PanelSlide.cs) so the two edges of
        // the window move identically.
        private void ApplyTreePanel(bool animate)
        {
            // Chevron points where the sidebar is going: left to tuck it away, right to bring
            // it back. Codepoints keep the source ASCII, as everywhere else in this project.
            SidebarToggleBtn.Content = ((char)(_treeOpen ? 0xE76B : 0xE76C)).ToString();

            // Nothing to grab while the panel is a zero-width column, and a live grip there
            // would sit on top of the rail.
            TreeResizeGrip.Visibility = _treeOpen ? Visibility.Visible : Visibility.Collapsed;

            TreeGapCol.Width = new GridLength(_treeOpen ? 6 : 0);

            // Left-hand panel, so its contents stay pinned to the LEFT edge during the tween.
            SlideColumn(TreeCol, TreePanel, _treeOpen,
                        _treeWidth, minOpen: TreeWidthMin, maxOpen: TreeWidthMax,
                        freezeAlign: HorizontalAlignment.Left, animate: animate);
        }
    }
}
