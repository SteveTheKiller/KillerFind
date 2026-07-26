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
        // The added number is the horizontal gap between tiles: at 96px art and the Comfortable
        // +16 that is a 112px cell, so icons sit 16px apart. It used to be a flat +44, which
        // spread the grid out with far more air between columns than Explorer uses. Longer
        // names trim sooner as a result - that is the trade, and the tooltip still carries the
        // full path. Density drives it now, so tightening pulls the columns together instead of
        // leaving the same gaps around smaller tiles.
        public double TileWidth  => Math.Max(96, _tileSize + TileExtraW[_density]);

        // The trailing number is everything under the art: the tile's own vertical padding, the
        // gap above the name, and two lines of it. Those three shrink with density, so the cell
        // has to shrink by the same amount or the space just moves from inside the tile to
        // between the rows and nothing looks any tighter.
        public double TileHeight => _tileSize + TileExtraH[_density];

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

        // ═══════════════════════════════════════════════════════════
        //  DENSITY
        // ═══════════════════════════════════════════════════════════
        // 0 Roomy, 1 Comfortable, 2 Compact, 3 Tight, 4 Minimal. Every view's padding is derived
        // here rather than hardcoded in its template, so one property change retightens all
        // three at once.
        //
        // Exposed as properties on this shared object rather than stamped onto every result the
        // way KillerNotes stamps its notes: KillerFind's list can hold six figures of rows and
        // walking them to set a padding would be absurd, while the templates already bind here
        // for the tile size. Changing a property repaints; nothing is re-listed.
        /// <summary>Number of density levels. The cycle and the status captions both key off it.</summary>
        public const int DensityLevels = 5;

        // Comfortable, not Roomy, is where a fresh install lands: it is the spacing every
        // screenshot and every previous build used, and level 0 exists to go LOOSER than that.
        private int _density = 1;

        public int Density
        {
            get => _density;
            set
            {
                int v = value < 0 ? 0 : value > DensityLevels - 1 ? DensityLevels - 1 : value;
                if (v == _density) return;
                _density = v;

                // Everything derived, in one go. TileHeight is in here because a tighter tile is
                // a SHORTER cell, not just a smaller picture - without it the grid keeps the old
                // row pitch and the padding comes off the inside of an unchanged box.
                Notify();
                Notify(nameof(TilePad));
                Notify(nameof(TileMargin));
                Notify(nameof(TileNamePad));
                Notify(nameof(TileWidth));
                Notify(nameof(TileHeight));
                Notify(nameof(RowPad));
                Notify(nameof(CardPad));
                Notify(nameof(HeaderPad));
            }
        }

        // The ladder, one row per level, written as tables rather than switch arms: with five
        // levels the point of the numbers is how they step, and a column you can read down
        // catches a value out of order in a way five separate expressions never would.
        //
        // Index: 0 Roomy, 1 Comfortable, 2 Compact, 3 Tight, 4 Minimal.

        // Tiles. The name keeps its two lines at every level - trimming a file name to one line
        // is lost information, which is the opposite of what density is for.
        private static readonly double[] TileExtraW = { 30, 16, 12, 8, 4 };
        private static readonly double[] TileExtraH = { 62, 52, 46, 40, 34 };

        private static readonly Thickness[] TilePads =
        {
            new Thickness(6, 8, 6, 8), new Thickness(4, 6, 4, 6), new Thickness(3, 4, 3, 4),
            new Thickness(2, 2, 2, 2), new Thickness(1, 1, 1, 1),
        };

        private static readonly Thickness[] TileMargins =
        {
            new Thickness(6), new Thickness(3), new Thickness(2), new Thickness(1), new Thickness(0),
        };

        private static readonly Thickness[] TileNamePads =
        {
            new Thickness(2, 8, 2, 0), new Thickness(2, 6, 2, 0), new Thickness(2, 4, 2, 0),
            new Thickness(2, 2, 2, 0), new Thickness(2, 1, 2, 0),
        };

        public Thickness TilePad     => TilePads[_density];
        public Thickness TileMargin  => TileMargins[_density];
        public Thickness TileNamePad => TileNamePads[_density];

        // Details rows and list cards. The side padding moves with density too, so a tight level
        // wins width as well as height - but the right number never drops below 22, because
        // that gap is what keeps the last column out from under the scrollbar rather than
        // decoration. The left number is shared with the column headers, which is why HeaderPad
        // exists: the two have to step together or every row sits off its own heading.
        private static readonly Thickness[] RowPads =
        {
            new Thickness(20, 5, 36, 5), new Thickness(14, 3, 30, 3), new Thickness(10, 1, 26, 1),
            new Thickness(8, 1, 24, 1),  new Thickness(6, 0, 22, 0),
        };

        private static readonly Thickness[] CardPads =
        {
            new Thickness(20, 9, 20, 9), new Thickness(14, 6, 14, 6), new Thickness(10, 4, 10, 4),
            new Thickness(8, 3, 8, 3),   new Thickness(6, 2, 6, 2),
        };

        // The header's own vertical padding is fixed - it is a band of type, not a row of
        // results, and letting it shrink would just make the column names harder to hit.
        private static readonly Thickness[] HeaderPads =
        {
            new Thickness(20, 4, 36, 4), new Thickness(14, 4, 30, 4), new Thickness(10, 4, 26, 4),
            new Thickness(8, 4, 24, 4),  new Thickness(6, 4, 22, 4),
        };

        public Thickness RowPad    => RowPads[_density];
        public Thickness CardPad   => CardPads[_density];
        public Thickness HeaderPad => HeaderPads[_density];

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

        // Folders and drives have no extension to key on, so without this the extension-only
        // fast path answers every one of them with the generic unknown-file page. It is what
        // picks up a custom folder icon too, and what makes a drive at This PC look like a
        // drive rather than a document.
        public static readonly DependencyProperty IsDirectoryProperty =
            DependencyProperty.RegisterAttached("IsDirectory", typeof(bool), typeof(TileArt),
                new PropertyMetadata(false, OnChanged));

        public static bool GetIsDirectory(DependencyObject d) => (bool)d.GetValue(IsDirectoryProperty);
        public static void SetIsDirectory(DependencyObject d, bool v) => d.SetValue(IsDirectoryProperty, v);

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
            img.Source = Services.IconCache.For(path!, size, GetIsDirectory(img));
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

        // The view mode is a WINDOW-wide setting mirrored into per-pane controls, so it has to
        // reach every live pane, not just the focused one (Panes.cs). Writing through `Pane`
        // alone left the second pane on whatever template its XAML defaulted to, with its three
        // view buttons unlit, and changing the view from one pane left the other stale.
        private void ApplyResultsView() => ForEachPane(ApplyResultsViewToPane);

        // Swap the panel and the template, then light the button that is now active. Same shape
        // as the folder picker's ApplyView, which is where the pattern comes from.
        private void ApplyResultsViewToPane()
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
