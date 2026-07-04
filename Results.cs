using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KillerFind.Models;

// Results-pane interactions: expand/collapse, sorting, the Ctrl+F quick filter, and
// piping results into a new tab. Partial of MainWindow.
namespace KillerFind
{
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════════════════════
        //  RESULT CLICK - single click expands, double click reveals
        // ═══════════════════════════════════════════════════════════
        private void ResultHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is SearchResult r)
                r.IsExpanded = !r.IsExpanded;
            ResultsList.SelectedItem = null;
            e.Handled = true;
        }

        private void ResultHeader_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is FrameworkElement el && el.Tag is string path && System.IO.File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            e.Handled = true;
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            bool expand = _active.Results.Any(r => !r.IsExpanded);
            foreach (var r in _active.Results) r.IsExpanded = expand;
            SetExpandAllLabel(expand);
        }

        // One glyph, two states: E740 expand-all / E73F collapse-all (codepoints keep
        // the source ASCII). The localized wording lives in the tooltip.
        private void SetExpandAllLabel(bool showCollapse)
        {
            ExpandAllGlyph.Text     = ((char)(showCollapse ? 0xE73F : 0xE740)).ToString();
            ExpandAllButton.ToolTip = Loc(showCollapse ? "Str_Btn_CollapseAll" : "Str_Btn_ExpandAll");
        }

        // ═══════════════════════════════════════════════════════════
        //  RESULT SORTING (like the HTML report's clickable columns)
        // ═══════════════════════════════════════════════════════════
        private bool _syncingSort;   // true while a tab switch programs the combo

        private void SortCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSort || _active == null || SortCombo.SelectedIndex < 0) return;
            _active.SortIndex = SortCombo.SelectedIndex;
            ApplySort(_active);
        }

        private void SortDir_Click(object sender, RoutedEventArgs e)
        {
            _active.SortAsc = !_active.SortAsc;
            ApplySort(_active);
        }

        // Sorts through the collection VIEW, so the underlying results (and the order
        // the engine found them in) are untouched; live batches insert sorted.
        private void ApplySort(SearchTab t)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(t.Results);
            view.SortDescriptions.Clear();
            string? prop = t.SortIndex switch
            {
                1 => nameof(SearchResult.FileName),
                2 => nameof(SearchResult.Directory),
                3 => nameof(SearchResult.SizeBytes),
                4 => nameof(SearchResult.Modified),
                _ => null,   // 0 = as found (Seq = discovery order)
            };
            // "as found" reverses too: descending on the discovery sequence.
            if (prop == null && !t.SortAsc) prop = nameof(SearchResult.Seq);
            if (prop != null)
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription(prop,
                    t.SortAsc ? System.ComponentModel.ListSortDirection.Ascending
                              : System.ComponentModel.ListSortDirection.Descending));
            // MDL2 chevron up / down, built from codepoints so the source stays pure ASCII.
            SortDirButton.Content = ((char)(t.SortAsc ? 0xE70E : 0xE70D)).ToString();
        }

        // ═══════════════════════════════════════════════════════════
        //  RESULTS QUICK-FILTER (Ctrl+F)
        // ═══════════════════════════════════════════════════════════
        // Slide the bar down out of the pane's top edge (VS Code find-widget style).
        private void ShowResultFilterBar()
        {
            // Restore the slid position (fraction of pane width, like KillerPDF's AnnotBarFrac).
            if (double.TryParse(Services.ThemeManager.GetSetting("FilterBarFrac"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double frac) &&
                ResultsPane.ActualWidth > 0)
            {
                double right = Math.Max(2, Math.Min(frac * ResultsPane.ActualWidth,
                                                    ResultsPane.ActualWidth - 80));
                ResultFilterBar.Margin = new Thickness(0, 0, right, 0);
            }
            ResultFilterBar.Visibility = Visibility.Visible;
            var tt = new System.Windows.Media.TranslateTransform();
            ResultFilterBar.RenderTransform = tt;
            var ease = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
            ResultFilterBar.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            ResultFilterBox.Focus();
            ResultFilterBox.SelectAll();
        }

        // Debounced like KillerPDF's search bar: re-filtering a huge result list on
        // every keystroke stutters, so wait for a 250ms pause in typing.
        private System.Windows.Threading.DispatcherTimer? _filterDebounce;

        private void ResultFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_active == null) return;
            _active.FilterText = ResultFilterBox.Text;

            if (_filterDebounce is null)
            {
                _filterDebounce = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(250) };
                _filterDebounce.Tick += (_, _) => { _filterDebounce!.Stop(); ApplyFilter(_active); };
            }
            _filterDebounce.Stop();
            _filterDebounce.Start();
        }

        private void ResultFilterClose_Click(object sender, RoutedEventArgs e)
        {
            ResultFilterBox.Text = string.Empty;   // TextChanged clears the view filter
            ResultFilterBar.Visibility = Visibility.Collapsed;
        }

        // Filters the collection VIEW by name or path - the underlying results are untouched.
        private void ApplyFilter(SearchTab t)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(t.Results);
            string q = t.FilterText.Trim();
            view.Filter = q.Length == 0
                ? null
                : o => o is SearchResult r &&
                       (r.FileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        r.Directory.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // 6-dot grip: slide the bar along the pane's top edge (like KillerPDF's
        // annotation bars). Position persists as a fraction of the pane width.
        private bool   _filterBarDrag;
        private double _filterBarGrabX;
        private double _filterBarStartRight;

        private void FilterGrip_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _filterBarDrag       = true;
            _filterBarGrabX      = e.GetPosition(ResultsPane).X;
            _filterBarStartRight = ResultFilterBar.Margin.Right;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void FilterGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_filterBarDrag) return;
            double dx = e.GetPosition(ResultsPane).X - _filterBarGrabX;
            double maxRight = Math.Max(2, ResultsPane.ActualWidth - ResultFilterBar.ActualWidth - 2);
            double right = Math.Min(maxRight, Math.Max(2, _filterBarStartRight - dx));
            ResultFilterBar.Margin = new Thickness(0, 0, right, 0);
        }

        private void FilterGrip_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_filterBarDrag) return;
            _filterBarDrag = false;
            ((UIElement)sender).ReleaseMouseCapture();
            if (ResultsPane.ActualWidth > 0)
                Services.ThemeManager.SetSetting("FilterBarFrac",
                    (ResultFilterBar.Margin.Right / ResultsPane.ActualWidth)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ═══════════════════════════════════════════════════════════
        //  PIPE - search within a search's results, in a new tab
        // ═══════════════════════════════════════════════════════════
        private void PipeButton_Click(object sender, RoutedEventArgs e) => PipeIntoNewTab(_active);

        private void PipeTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SearchTab t) PipeIntoNewTab(t);
        }

        private void PipeIntoNewTab(SearchTab src)
        {
            // Pipe exactly what the user SEES: the collection view, so an active
            // Ctrl+F filter narrows what flows into the next search.
            var files = System.Windows.Data.CollectionViewSource.GetDefaultView(src.Results)
                .Cast<object>().OfType<SearchResult>().Select(r => r.FilePath).ToList();
            if (files.Count == 0)
            {
                SetTabStatusKey(_active, "Str_Status_NoPipe");
                return;
            }

            CaptureTab(_active);
            var t = CreateTab();
            var firstTerm = src.Groups.SelectMany(g => g.Terms)
                .Select(x => x.Pattern.Trim()).FirstOrDefault(p => p.Length > 0);
            string query = string.IsNullOrEmpty(src.QueryLabel)
                ? (firstTerm ?? string.Empty)
                : src.QueryLabel;

            t.PipeFiles = files;
            t.RootPath  = src.RootPath;
            // "375 results from ~\code  |  name: steve" - the query that produced
            // them makes the breadcrumb self-explanatory. Args stored raw so a
            // language switch can re-render the breadcrumb.
            t.PipeArgs  = [files.Count.ToString("N0"),
                string.IsNullOrEmpty(src.Title) ? src.RootPath : src.Title, query];
            t.PipeLabel = string.Format(Loc("Str_Pipe_Scope"), t.PipeArgs);

            // Tab title keeps the lineage readable: "~\code > steve".
            t.Title = $"{src.Title} > {(string.IsNullOrEmpty(firstTerm) ? files.Count.ToString("N0") : firstTerm)}";
            ActivateTab(t);
        }

        // ═══════════════════════════════════════════════════════════
        //  ROW ACTIONS (context menu + the inline row buttons)
        // ═══════════════════════════════════════════════════════════
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            // FrameworkElement, not MenuItem: the inline row buttons share this handler.
            if (sender is FrameworkElement fe && fe.Tag is string path && System.IO.File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            // The Windows "Open with" chooser. OpenAs_RunDLL takes the rest of the
            // command line as the path - no quotes, even with spaces.
            if (sender is MenuItem mi && mi.Tag is string path && System.IO.File.Exists(path))
                System.Diagnostics.Process.Start("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}");
        }

        private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path && System.IO.File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}
