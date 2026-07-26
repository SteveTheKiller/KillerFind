using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace KillerFind
{
    // Results pane view modes: 0 list (the expandable cards), 1 icons (tile grid), 2 details
    // (flat rows under sortable column headers). Partial of MainWindow.
    //
    // Sorting and filtering are untouched by any of this. Both live on the collection view
    // (Results.cs ApplySort / ApplyFilter), so they keep working across every layout for free -
    // switching view swaps the panel and the template and nothing else.

    /// <summary>
    /// Shared, bindable tile geometry. The wrap panel binds its ItemWidth/ItemHeight here and
    /// every tile binds its icon size here, so one property change resizes the whole grid.
    /// A single app-wide instance: the tile size is a view preference, not per tab, which is
    /// also how Explorer treats it.
    /// </summary>
    public sealed class ResultsViewState : INotifyPropertyChanged
    {
        public static ResultsViewState Current { get; } = new ResultsViewState();

        // The sizes the shell can actually serve well. 32 and 48 map to real shell icon sizes;
        // above that comes from the jumbo list (see Services/IconCache.cs).
        public static readonly int[] Steps = { 32, 48, 64, 96, 128, 192, 256 };

        private int _tileSize = 96;

        public int TileSize
        {
            get => _tileSize;
            set
            {
                int v = Math.Max(Steps[0], Math.Min(Steps[Steps.Length - 1], value));
                if (v == _tileSize) return;
                _tileSize = v;
                Notify();
                Notify(nameof(TileWidth));
                Notify(nameof(TileHeight));
            }
        }

        // Room for the art plus two wrapped lines of filename. The width floor keeps small
        // icon sizes from squeezing names down to three characters and an ellipsis.
        //
        // The +16 is the horizontal gap between tiles: at 96px art that is a 112px cell, so
        // icons sit 16px apart. It used to be +44, which spread the grid out with far more air
        // between columns than Explorer uses. Longer names trim sooner as a result - that is
        // the trade, and the tooltip still carries the full path.
        public double TileWidth  => Math.Max(96, _tileSize + 16);
        public double TileHeight => _tileSize + 52;

        // Details view's "location" column. Zero while browsing, where it repeats the folder you
        // are already standing in on every single row; restored for search results, which is the
        // one case where rows come from different places and the column earns its width.
        //
        // Lives here rather than on the tab because both the header Grid and every row's Grid
        // need the same value, and a DataTemplate's Grid can only reach a shared source.
        private GridLength _locationWidth = SearchLocationWidth;

        internal static readonly GridLength SearchLocationWidth = new GridLength(1.3, GridUnitType.Star);

        public GridLength LocationWidth
        {
            get => _locationWidth;
            set { if (_locationWidth != value) { _locationWidth = value; Notify(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Attached behavior that puts the right picture in a tile's Image.
    /// <para>
    /// A plain <c>{Binding Icon}</c> cannot do this job. The image depends on two things that
    /// change independently - the file and the current tile size - and containers are recycled,
    /// so the same Image element is handed a different file as the grid scrolls. Setting Source
    /// from a callback on both attached properties covers all of it: a rebind and a size change
    /// look the same from here, and whichever fires last wins.
    /// </para>
    /// </summary>
    public static class TileArt
    {
        public static readonly DependencyProperty PathProperty =
            DependencyProperty.RegisterAttached("Path", typeof(string), typeof(TileArt),
                new PropertyMetadata(null, OnChanged));

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.RegisterAttached("Size", typeof(int), typeof(TileArt),
                new PropertyMetadata(0, OnChanged));

        public static string? GetPath(DependencyObject d) => (string?)d.GetValue(PathProperty);
        public static void   SetPath(DependencyObject d, string? v) => d.SetValue(PathProperty, v);

        public static int  GetSize(DependencyObject d) => (int)d.GetValue(SizeProperty);
        public static void SetSize(DependencyObject d, int v) => d.SetValue(SizeProperty, v);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image img) return;

            string? path = GetPath(img);
            int     size = GetSize(img);

            if (string.IsNullOrEmpty(path) || size <= 0) { img.Source = null; return; }

            // Synchronous and cheap: the shell icon is cached per extension and per size, so a
            // screen of tiles costs a handful of shell calls no matter how many results there are.
            img.Source = Services.IconCache.For(path!, size);
        }
    }

    public partial class MainWindow
    {
        private int _viewMode;   // 0 list, 1 icons, 2 details

        // The card template stays inline on the ListBox in MainWindow.xaml rather than becoming a
        // keyed resource like the other two: it is ninety lines of nested markup and moving it
        // would be a large diff for no gain. Grab it once on the way past instead.
        private DataTemplate? _listTemplate;

        private void InitResultsView()
        {
            _listTemplate ??= Pane.ResultsList.ItemTemplate;

            if (int.TryParse(Services.ThemeManager.GetSetting("ResultsView"), out int v) && v >= 0 && v <= 2)
                _viewMode = v;

            if (int.TryParse(Services.ThemeManager.GetSetting("ResultsTileSize"), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out int px))
                ResultsViewState.Current.TileSize = px;

            ApplyResultsView();
        }

        internal void ViewList_Click(object sender, RoutedEventArgs e)    => SetResultsView(0);
        internal void ViewIcons_Click(object sender, RoutedEventArgs e)   => SetResultsView(1);
        internal void ViewDetails_Click(object sender, RoutedEventArgs e) => SetResultsView(2);

        private void SetResultsView(int mode)
        {
            if (_viewMode == mode) return;
            _viewMode = mode;
            ApplyResultsView();
            Services.ThemeManager.SetSetting("ResultsView", mode.ToString(CultureInfo.InvariantCulture));
        }

        // Swap the panel and the template, then light the button that is now active. Same shape
        // as the folder picker's ApplyView, which is where the pattern comes from.
        private void ApplyResultsView()
        {
            Pane.ResultsList.ItemsPanel = (ItemsPanelTemplate)Pane.ResultsList.FindResource(
                _viewMode == 1 ? "PanelWrap" : "PanelStack");

            Pane.ResultsList.ItemTemplate =
                _viewMode == 1 ? (DataTemplate)Pane.ResultsList.FindResource("TileTemplate") :
                _viewMode == 2 ? (DataTemplate)Pane.ResultsList.FindResource("DetailsRowTemplate")
                               : _listTemplate;

            // Column headers belong to details view; expand/collapse-all only means anything for
            // the cards, which are the only layout with something to expand.
            //
            // Hidden, not Collapsed: the button sits in the header's right-hand strip, and a
            // collapsed element gives up its width, so every other control in that strip slid
            // sideways each time the view changed. Hidden keeps the slot.
            Pane.DetailsHeader.Visibility   = _viewMode == 2 ? Visibility.Visible : Visibility.Collapsed;
            Pane.ExpandAllButton.Visibility = _viewMode == 0 ? Visibility.Visible : Visibility.Hidden;

            Pane.ViewListBtn.Tag    = _viewMode == 0 ? "on" : null;
            Pane.ViewIconsBtn.Tag   = _viewMode == 1 ? "on" : null;
            Pane.ViewDetailsBtn.Tag = _viewMode == 2 ? "on" : null;

            UpdateColumnArrows();
        }

        // ── Sortable column headers (details view) ───────────────
        // These drive the same SortIndex / SortAsc the combo does, so the two controls are always
        // showing the same thing and ApplySort stays the single place sorting happens.
        internal void ColName_Click(object sender, RoutedEventArgs e)     => SetColumnSort(1);
        internal void ColFolder_Click(object sender, RoutedEventArgs e)   => SetColumnSort(2);
        internal void ColSize_Click(object sender, RoutedEventArgs e)     => SetColumnSort(3);
        internal void ColModified_Click(object sender, RoutedEventArgs e) => SetColumnSort(4);

        private void SetColumnSort(int index)
        {
            if (_active == null) return;

            if (_active.SortIndex == index)
            {
                _active.SortAsc = !_active.SortAsc;
            }
            else
            {
                _active.SortIndex = index;
                // Text sorts want A first; size and date want the biggest and newest first, which
                // is what you are looking for when you click those.
                _active.SortAsc = index == 1 || index == 2;
            }

            _syncingSort = true;
            Pane.SortCombo.SelectedIndex = _active.SortIndex;
            _syncingSort = false;

            ApplySort(_active);
        }

        // Same MDL2 chevrons the sort-direction button uses, built from codepoints so the
        // source stays ASCII (the convention across this project).
        private static readonly string ArrowUp   = ((char)0xE70E).ToString();
        private static readonly string ArrowDown = ((char)0xE70D).ToString();

        private void UpdateColumnArrows()
        {
            if (_active == null) return;
            string a = _active.SortAsc ? ArrowUp : ArrowDown;
            Pane.ColNameArrow.Text   = _active.SortIndex == 1 ? a : string.Empty;
            Pane.ColFolderArrow.Text = _active.SortIndex == 2 ? a : string.Empty;
            Pane.ColSizeArrow.Text   = _active.SortIndex == 3 ? a : string.Empty;
            Pane.ColModArrow.Text    = _active.SortIndex == 4 ? a : string.Empty;
        }

        // ── Icon sizing (Ctrl+wheel over the results pane) ───────
        // Explorer's gesture, and it is free here: the app-wide zoom is the wheel over the
        // title-bar wordmark with no modifier (AppScale.cs), so the two never meet. Steps are
        // discrete because the shell only has a few real icon sizes to give - sliding smoothly
        // between them would just be resampling the same bitmap.
        internal void ResultsList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (_viewMode != 1) return;   // only the tile grid has a size to change
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;

            var steps = ResultsViewState.Steps;
            int i = Array.IndexOf(steps, ResultsViewState.Current.TileSize);
            if (i < 0)
            {
                // Restored from a setting that is not on the ladder: snap to the nearest step.
                i = 0;
                for (int k = 1; k < steps.Length; k++)
                    if (Math.Abs(steps[k] - ResultsViewState.Current.TileSize) <
                        Math.Abs(steps[i] - ResultsViewState.Current.TileSize)) i = k;
            }

            i = Math.Max(0, Math.Min(steps.Length - 1, i + (e.Delta > 0 ? 1 : -1)));
            ResultsViewState.Current.TileSize = steps[i];

            Services.ThemeManager.SetSetting("ResultsTileSize",
                steps[i].ToString(CultureInfo.InvariantCulture));

            e.Handled = true;   // do not also scroll the list
        }
    }
}
