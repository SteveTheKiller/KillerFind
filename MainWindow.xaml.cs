using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using KillerFind.Models;

namespace KillerFind
{
    // Search core: ctor/wiring, the scope picker, term/filter handlers, the search
    // loop, and engine callbacks. Everything else is partials:
    //   Tabs.cs      - tab lifecycle + strip drag physics
    //   Session.cs   - install flow, smart-Esc quit, tab persistence
    //   Results.cs   - result interactions, sorting, quick filter, pipe
    //   Export.cs    - CSV / HTML export
    //   Chrome.cs / ThemeFlyout.cs / About.cs / Language.cs - shell, theme, about, locale
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<SearchTab> _tabs = [];
        private SearchTab _active = null!;   // set in the ctor before anything reads it

        public MainWindow()
        {
            InitializeComponent();

            // KillerUI / Grunge shell wiring.
            SourceInitialized += MainWindow_SourceInitialized;   // Chrome.cs
            ApplyGrainTexture();                                 // Chrome.cs
            Loaded += (_, _) => FadeInContent();                 // Chrome.cs
            UpdateThemeSwatchSelection();                        // ThemeFlyout.cs
            UpdateAccentSwatches();
            Services.ThemeManager.ThemeChanged += () => { UpdateThemeSwatchSelection(); UpdateAccentSwatches(); };

            var ver = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            // Demo used to fake "v1.0.0" pre-release; versions are real now, so always show the truth.
            VersionLabel.Text = $"v{ver}";

            // Titlebar + About icons: kf-icon.ico is multi-size, so pick the frame nearest
            // each display size (a raw Image Source=.ico can grab the 16px frame and
            // upscale it blurry - that was the mangled About icon).
            try
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri("pack://application:,,,/Resources/kf-icon.ico"),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                TitleIcon.Source = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
                AboutIcon.Source = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 64)).First();
            }
            catch { /* icon missing - wordmark alone is fine */ }

            Pane.TabStrip.ItemsSource = _tabs;
            if (DemoMode || !TryRestoreTabs()) ActivateTab(CreateTab());   // Session.cs / Tabs.cs

            // Restore the saved app-wide accessibility size (AppScale.cs). After the tabs so
            // _active exists, though the restore path never writes a status line.
            InitAppScale();

            // Restore the saved results view mode and tile size (ResultsView.cs). Also after the
            // tabs, since applying the view reads _active to redraw the sort arrows.
            InitResultsView();

            // Search is an optional panel now, closed unless it was left open (SearchPanel.cs).
            InitSearchPanel();

            // Folder tree on the left, open unless it was closed (TreePanel.cs). Roots are the
            // ready drives; everything below loads on expand (FolderTree.cs).
            InitFolderTree();
            InitTreePanel();

            // Saved locations, in the slide-up under the tree (Bookmarks.cs). After the tree so
            // its panel row exists, and after the tabs so the star can read the active folder.
            InitBookmarks();

            // Show-hidden and folders-on-top (ViewOptions.cs). Before nothing in particular -
            // they are read by the listing and the tree, both of which run later.
            InitViewOptions();

            // Where new tabs open (AddressBar.cs).
            InitHomeFolder();

            Loaded += (_, _) =>
            {
                // Demo mode: no install badge (and fabricated tabs, DemoMode.cs).
                if (App.IsPortable() && !DemoMode) PortableBadge.Visibility = Visibility.Visible;
                if (DemoMode) GenerateDemoData();

                // A first-run tab starts at Home rather than as an empty search form. Deferred
                // to Loaded rather than done in the ctor because navigating reveals the folder
                // in the tree, and the tree's roots are not built until InitFolderTree above has
                // run. Restored tabs and piped tabs are left exactly as they were.
                if (!DemoMode && !_active.IsBrowsing
                    && _active.PipeFiles == null
                    && string.IsNullOrEmpty(_active.RootPath))
                {
                    _ = NavigateTo(HomeFolder);   // Browse.cs
                }
            };
            Closing += (_, _) =>
            {
                StopWatching();                            // BrowseWatcher.cs
                if (!DemoMode) SaveTabsOnExit();           // Session.cs
            };

            // The theme flyout is StaysOpen (so scrolling under it works); close it
            // ourselves on any click outside it or when the window loses focus.
            PreviewMouseDown += (_, e) =>
            {
                if (!ThemePopup.IsOpen) return;
                if (ThemePopup.Child is FrameworkElement c && c.IsMouseOver) return;
                if (ThemeButton.IsMouseOver) return;   // its own click handles the toggle
                ThemePopup.IsOpen = false;
            };
            Deactivated += (_, _) => ThemePopup.IsOpen = false;
            // Popups don't follow their placement target - close on any window move/resize
            // so the flyout can't float detached in space.
            LocationChanged += (_, _) => ThemePopup.IsOpen = false;
            SizeChanged     += (_, _) => ThemePopup.IsOpen = false;
            StateChanged    += (_, _) => ThemePopup.IsOpen = false;
        }

        private void VersionLabel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowAboutOverlay();  // About.cs

        private void Wordmark_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://killerfind.net") { UseShellExecute = true });
            e.Handled = true;
        }

        // ═══════════════════════════════════════════════════════════
        //  SCOPE - folder picker
        // ═══════════════════════════════════════════════════════════
        // Clicking the location row now starts an address edit instead of opening the picker;
        // that handler lives in AddressBar.cs. The picker is still reachable from Ctrl+O and
        // from the search panel's own browse button below.
        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
            => OpenFolderPicker();

        private void OpenFolderPicker()
        {
            var dlg = new FolderPickerDialog(Pane.RootPathBox.Text) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string? picked = dlg.SelectedPath;
            if (picked is null || picked.Length == 0) return;

            Pane.RootPathBox.Text    = picked;
            Pane.ScopePathLabel.Text = picked;
            _active.RootPath    = picked;
            _active.Title       = ToTabTitle(picked);
            // Picking a folder is the escape hatch from a piped scope.
            _active.PipeFiles   = null;
            _active.PipeLabel   = string.Empty;

            // Picking a folder now GOES there as well as scoping the search to it. That is the
            // whole shift: the folder you are looking at and the folder a search would cover are
            // the same folder, so there is nothing to keep in sync.
            _ = NavigateTo(picked);   // Browse.cs
        }

        // Tab title = the search location, home-relative: C:\Users\steve\code -> ~\code.
        // Distinct folders under home stay distinct instead of all collapsing to a leaf name.
        private static string ToTabTitle(string path)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            {
                var rest = path[home.Length..].TrimStart('\\');
                return rest.Length == 0 ? "~" : "~\\" + rest;
            }
            return path;
        }

        // ═══════════════════════════════════════════════════════════
        //  SEARCH TERMS
        // ═══════════════════════════════════════════════════════════
        private void AddTerm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TermGroup g)
                g.Terms.Add(new SearchTerm());
        }

        private void RemoveTerm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTerm term)
            {
                var groups = _active.Groups;
                var g = groups.FirstOrDefault(gr => gr.Terms.Contains(term));
                if (g == null) return;
                if (groups.Sum(gr => gr.Terms.Count) > 1) g.Terms.Remove(term);
                if (g.Terms.Count == 0 && groups.Count > 1) groups.Remove(g);
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            var g = new TermGroup();
            g.Terms.Add(new SearchTerm());
            _active.Groups.Add(g);
        }

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TermGroup g && _active.Groups.Count > 1)
                _active.Groups.Remove(g);
        }

        private void ToggleGroupMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TermGroup g)
                g.Mode = g.Mode == TermGroup.GroupMode.Or ? TermGroup.GroupMode.And : TermGroup.GroupMode.Or;
        }

        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTerm term)
                term.Mode = term.Mode == SearchTerm.SearchMode.FileName
                    ? SearchTerm.SearchMode.Content
                    : SearchTerm.SearchMode.FileName;
        }

        // ═══════════════════════════════════════════════════════════
        //  FILTERS
        // ═══════════════════════════════════════════════════════════
        private void AddFilter_Click(object sender, RoutedEventArgs e)
            => _active.Filters.Add(new SearchFilter());

        private void RemoveFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchFilter f)
                _active.Filters.Remove(f);
        }

        private void ToggleFilterCondition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchFilter f)
                f.ConditionIndex = f.ConditionIndex == 0 ? 1 : 0;
        }

        // "advanced" accordion (include/exclude/case): slides open/closed by animating
        // MaxHeight (150ms, eased) with an opacity fade riding along.
        private void AdvancedToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            bool show = AdvancedPanel.Visibility != Visibility.Visible;
            // MDL2 chevron down / right, from codepoints so the source stays ASCII.
            AdvancedChevron.Text = ((char)(show ? 0xE70D : 0xE76C)).ToString();

            var ease = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            if (show)
            {
                AdvancedPanel.Visibility = Visibility.Visible;
                AdvancedPanel.Measure(new Size(AdvancedPanel.ActualWidth > 0 ? AdvancedPanel.ActualWidth : 300,
                                               double.PositiveInfinity));
                double target = AdvancedPanel.DesiredSize.Height;
                var grow = new System.Windows.Media.Animation.DoubleAnimation(0, target,
                    TimeSpan.FromMilliseconds(Anim.FadeMs)) { EasingFunction = ease };
                grow.Completed += (_, _) =>
                {
                    AdvancedPanel.BeginAnimation(MaxHeightProperty, null);
                    AdvancedPanel.MaxHeight = double.PositiveInfinity;
                };
                AdvancedPanel.BeginAnimation(MaxHeightProperty, grow);
                AdvancedPanel.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Anim.FadeMs)));
            }
            else
            {
                var shrink = new System.Windows.Media.Animation.DoubleAnimation(AdvancedPanel.ActualHeight, 0,
                    TimeSpan.FromMilliseconds(Anim.FadeMs)) { EasingFunction = ease };
                shrink.Completed += (_, _) =>
                {
                    AdvancedPanel.Visibility = Visibility.Collapsed;
                    AdvancedPanel.BeginAnimation(MaxHeightProperty, null);
                    AdvancedPanel.MaxHeight = double.PositiveInfinity;
                };
                AdvancedPanel.BeginAnimation(MaxHeightProperty, shrink);
                AdvancedPanel.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Anim.FadeMs)));
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATTERN HELP CARD
        // ═══════════════════════════════════════════════════════════
        private void PatternHelp_Click(object sender, RoutedEventArgs e)
        {
            PatternHelpOverlay.Visibility = Visibility.Visible;
            Anim.FadeIn(PatternHelpOverlay);   // the standard 150ms fade, like About
        }

        private void PatternHelpClose_Click(object sender, RoutedEventArgs e)
            => FadeOverlayOut(PatternHelpOverlay);   // About.cs helper

        private void PatternHelpOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => FadeOverlayOut(PatternHelpOverlay);

        private void PatternHelpCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => e.Handled = true;   // clicks on the card don't dismiss it

        // ═══════════════════════════════════════════════════════════
        //  SHORTCUTS CARD (F1) - family standard, same shape as above
        // ═══════════════════════════════════════════════════════════
        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            // Restores whichever view you last had open, and builds it on first use
            // (KeyboardMapOverlay.cs / ShortcutsOverlay.cs).
            ApplyPersistedShortcutView();

            ShortcutsOverlay.Visibility = Visibility.Visible;
            Anim.FadeIn(ShortcutsOverlay);
        }

        private void ShortcutsClose_Click(object sender, RoutedEventArgs e)
            => FadeOverlayOut(ShortcutsOverlay);   // About.cs helper

        private void ShortcutsOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => FadeOverlayOut(ShortcutsOverlay);

        private void ShortcutsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => e.Handled = true;   // clicks on the card don't dismiss it

        // ═══════════════════════════════════════════════════════════
        //  SEARCH / STOP  (per tab - background tabs keep searching)
        // ═══════════════════════════════════════════════════════════
        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            var tab = _active;

            if (tab.IsSearching)
            {
                tab.Cts?.Cancel();
                return;
            }

            CaptureTab(tab);

            string root = tab.RootPath.Trim();
            if (tab.PipeFiles == null && (string.IsNullOrEmpty(root) || !Directory.Exists(root)))
            {
                // No folder picked yet? Don't scold - open the picker and carry on.
                OpenFolderPicker();
                root = tab.RootPath.Trim();
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            }

            var activeGroups = tab.Groups
                .Where(g => g.Terms.Any(t => !string.IsNullOrWhiteSpace(t.Pattern)))
                .ToList();
            var activeFilters = tab.Filters.Where(f => f.IsActive).ToList();
            if (activeGroups.Count == 0 && activeFilters.Count == 0)
            {
                SetTabStatusKey(tab, "Str_Status_NoTerms");
                return;
            }

            tab.Results.Clear();
            foreach (var t in tab.Groups.SelectMany(g => g.Terms)) t.ResetCount();
            tab.ScannedLabel = string.Empty;
            tab.StatsLabel   = string.Empty;
            tab.QueryLabel   = BuildQueryLabel(activeGroups, activeFilters);
            tab.IsSearching  = true;
            ApplySort(tab);   // strips the view sort for the run - see ApplySort
            if (tab == _active)
            {
                ScannedText.Text       = string.Empty;
                ScannedText.Visibility = Visibility.Visible;
                StatsText.Text         = string.Empty;
                QueryText.Text         = tab.QueryLabel;
                ResultsHeader.Text     = Loc("Str_Lbl_Results");
                SearchButton.Content   = Loc("Str_Btn_Stop");
                SetExpandAllLabel(false);
            }

            tab.Cts = new CancellationTokenSource();
            var sw = Stopwatch.StartNew();

            try
            {
                await tab.Engine.SearchAsync(
                    root, activeGroups, activeFilters, tab.IncludePatterns, tab.ExcludePatterns,
                    tab.CaseSensitive, tab.Cts.Token, tab.PipeFiles);

                // The engine's final batch is still queued on the dispatcher at this point -
                // let it land BEFORE reading Results.Count, or "Done - 0 file(s) matched"
                // shows next to a full list.
                await Dispatcher.InvokeAsync(() => { },
                    System.Windows.Threading.DispatcherPriority.Background);

                sw.Stop();
                if (tab.Cts.IsCancellationRequested)
                    SetTabStatusKey(tab, "Str_Status_Stopped");
                else
                    SetTabStatusKey(tab, "Str_Status_Done",
                        sw.Elapsed.TotalSeconds.ToString("0.00"), tab.Results.Count);
            }
            catch (OperationCanceledException)
            {
                SetTabStatusKey(tab, "Str_Status_Stopped");
            }
            finally
            {
                tab.IsSearching = false;
                ApplySort(tab);   // the run's deferred sort lands here, in one pass
                if (tab == _active)
                {
                    SearchButton.Content   = Loc("Str_Btn_Search");
                    ScannedText.Visibility = Visibility.Collapsed;
                    ResultsHeader.Text     = string.Format(Loc("Str_Lbl_ResultsCount"), tab.Results.Count);
                }
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var tab = _active;
            tab.Results.Clear();
            foreach (var t in tab.Groups.SelectMany(g => g.Terms)) t.ResetCount();
            tab.ScannedLabel = string.Empty;
            tab.StatsLabel   = string.Empty;
            tab.QueryLabel   = string.Empty;
            ScannedText.Visibility = Visibility.Collapsed;
            StatsText.Text         = string.Empty;
            QueryText.Text         = string.Empty;
            ResultsHeader.Text     = Loc("Str_Lbl_Results");
            SetTabStatusKey(tab, "Str_Status_Cleared");
        }

        // "name: steve  OR  content: foo  |  extension is pdf, over 100 MB" - built from
        // the same localized words the dropdowns show.
        private string BuildQueryLabel(List<TermGroup> groups, List<SearchFilter> filters)
        {
            var parts = new List<string>();
            foreach (var g in groups)
            {
                var terms = g.Terms.Where(t => !string.IsNullOrWhiteSpace(t.Pattern))
                    .Select(t => $"{t.ModeName}: {t.Pattern.Trim()}");
                string joiner = g.Mode == TermGroup.GroupMode.And
                    ? $"  {Loc("Str_Join_And")}  " : $"  {Loc("Str_Join_Or")}  ";
                parts.Add(string.Join(joiner, terms));
            }
            string q = string.Join("  +  ", parts.Where(p => p.Length > 0));

            var fparts = filters.Select(DescribeFilter).Where(s => s.Length > 0).ToList();
            if (fparts.Count > 0)
                q = q.Length > 0 ? $"{q}  |  {string.Join(", ", fparts)}" : string.Join(", ", fparts);
            return q;
        }

        private string DescribeFilter(SearchFilter f) => f.FieldIndex switch
        {
            SearchFilter.FieldExt =>
                $"{Loc("Str_Filter_Ext")} {Loc(f.ConditionIndex == 0 ? "Str_Cond_Is" : "Str_Cond_IsNot")} {f.Text.Trim()}",
            SearchFilter.FieldDate => f.Date.HasValue
                ? $"{Loc(f.ConditionIndex == 0 ? "Str_Cond_Before" : "Str_Cond_After")} {f.Date.Value:yyyy-MM-dd}"
                : string.Empty,
            _ =>
                $"{Loc(f.ConditionIndex == 0 ? "Str_Cond_Larger" : "Str_Cond_Smaller")} {f.SizeText.Trim()} {(f.UnitIndex == SearchFilter.UnitMb ? "MB" : "KB")}",
        };

        // Releasing a modifier drops the keyboard preview back a layer. Nothing else listens for
        // key-up; this exists purely so the board follows the hand (KeyboardMapOverlay.cs).
        private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
            => KbSyncLayerFromModifiers();

        // Global keys: Enter runs the search, Esc closes the filter bar or stops a
        // running search, Ctrl+F opens the results quick-filter.
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var mods  = System.Windows.Input.Keyboard.Modifiers;
            bool ctrl  = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
            bool shift = (mods & System.Windows.Input.ModifierKeys.Shift)   != 0;
            bool alt   = (mods & System.Windows.Input.ModifierKeys.Alt)     != 0;

            // Holding a modifier previews that layer on the visual keyboard, so a chord can be
            // found by pressing Ctrl rather than by reading. No-op unless the board is showing
            // (KeyboardMapOverlay.cs), and deliberately BEFORE the handling below so it still
            // runs for chords that go on to be swallowed.
            KbSyncLayerFromModifiers();

            // Alt+1-0 jumps to a saved location. Alt chords arrive as Key.System with the real
            // key parked in SystemKey, so they have to be unwrapped before anything can match -
            // and they are checked first, ahead of every e.Key test below, which would all see
            // Key.System and never fire. NumPad is deliberately excluded: Alt+numpad digits are
            // Windows' own character-entry sequence.
            if (alt && !ctrl && !shift)
            {
                var real = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                if (real >= System.Windows.Input.Key.D0 && real <= System.Windows.Input.Key.D9)
                {
                    // 0 is the tenth slot, the way it sits last on the number row.
                    int slot = real == System.Windows.Input.Key.D0 ? 10 : real - System.Windows.Input.Key.D0;
                    JumpToBookmark(slot);   // Bookmarks.cs
                    e.Handled = true;
                    return;
                }

                // Alt+D is Explorer's address-bar chord and costs nothing to honour alongside
                // Ctrl+L, so muscle memory from either lineage works.
                if (real == System.Windows.Input.Key.D)
                {
                    BeginEditAddress();   // AddressBar.cs
                    e.Handled = true;
                    return;
                }

                // Alt+Left / Right / Up: Explorer's navigation chords. These had no binding at
                // all - Back, Forward and Up were reachable only by clicking the toolbar, which
                // is the first thing a hand trained on Explorer reaches for and misses.
                if (real == System.Windows.Input.Key.Left)
                {
                    NavBack_Click(this, new RoutedEventArgs());      // Browse.cs
                    e.Handled = true;
                    return;
                }
                if (real == System.Windows.Input.Key.Right)
                {
                    NavForward_Click(this, new RoutedEventArgs());   // Browse.cs
                    e.Handled = true;
                    return;
                }
                if (real == System.Windows.Input.Key.Up)
                {
                    NavUp_Click(this, new RoutedEventArgs());        // Browse.cs
                    e.Handled = true;
                    return;
                }
            }

            // Backspace goes Back, the way it always has in Explorer. Guarded on text input, or
            // it would eat the character you were deleting in the address bar or a term box.
            if (!ctrl && !alt && e.Key == System.Windows.Input.Key.Back
                && e.OriginalSource is not TextBox && e.OriginalSource is not ComboBox)
            {
                NavBack_Click(this, new RoutedEventArgs());   // Browse.cs
                e.Handled = true;
                return;
            }

            if (ctrl && !shift && e.Key == System.Windows.Input.Key.B)
            {
                BookmarksBtn_Click(this, new RoutedEventArgs());   // Bookmarks.cs
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F && ctrl && !shift)
            {
                ShowResultFilterBar();   // Results.cs
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F && ctrl && shift)
            {
                PipeButton_Click(this, new RoutedEventArgs());   // Results.cs
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.S && ctrl && shift)
            {
                // Search is an optional panel now, so it needs a way in from the keyboard
                // (SearchPanel.cs). The chevron's tooltip names this chord.
                ToggleSearchPanel();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F1)
            {
                // F1: the shortcuts card, same as every other app in the family.
                Shortcuts_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F5)
            {
                // Explorer's Refresh, and it resolves itself: F5 refreshes whatever the tab is
                // showing. A browsed folder re-lists off disk, a search tab re-runs its search -
                // which is what F5 already did, so nothing was taken away. Enter still runs a
                // search from the panel.
                if (_active != null && _active.IsBrowsing && !string.IsNullOrEmpty(_active.CurrentFolder))
                    _ = NavigateTo(_active.CurrentFolder!, record: false);   // Browse.cs
                else
                    Search_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F4 && !ctrl && !shift && !alt)
            {
                // Explorer's address-bar key, alongside Ctrl+L and Alt+D.
                BeginEditAddress();   // AddressBar.cs
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F9)
            {
                Export_Click(this, new RoutedEventArgs());      // Export.cs - HTML
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F8)
            {
                ExportCsv_Click(this, new RoutedEventArgs());   // Export.cs - CSV
                e.Handled = true;
            }
            else if (ctrl && !shift && e.Key == System.Windows.Input.Key.A
                     && e.OriginalSource is not TextBox)
            {
                // Select every row. Skipped inside a text box, where Ctrl+A has to keep meaning
                // "select this text".
                Pane.ResultsList.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F12)
            {
                // F12: the About card, same as KillerPDF. It was previously reachable only by
                // clicking the version in the footer, which nobody finds by accident.
                ShowAboutOverlay();   // About.cs
                e.Handled = true;
            }
            else if (ctrl && !shift && e.Key == System.Windows.Input.Key.E)
            {
                // Explorer's Ctrl+E puts the caret in the search box, so this does too. Export
                // moved off it to F9 / F8 (single keys, which is the family preference anyway).
                FocusSearchTerms();   // SearchPanel.cs
                e.Handled = true;
            }
            else if (ctrl && (e.Key == System.Windows.Input.Key.Right || e.Key == System.Windows.Input.Key.Left)
                     && !(e.OriginalSource is TextBox))
            {
                // Explicit expand / collapse - the toolbar button toggles, these don't.
                // Skipped inside a TextBox so Ctrl+arrow keeps its word-jump meaning there.
                bool expand = e.Key == System.Windows.Input.Key.Right;
                foreach (var r in _active.Results) r.IsExpanded = expand;
                SetExpandAllLabel(expand);   // Results.cs
                e.Handled = true;
            }
            else if (ctrl && !shift && e.Key == System.Windows.Input.Key.L)
            {
                // Ctrl+L is the address bar in Explorer and in every browser, and this is a
                // shell now, so it goes there. Clear moved to Ctrl+Shift+L (AddressBar.cs).
                BeginEditAddress();
                e.Handled = true;
            }
            else if (ctrl && shift && e.Key == System.Windows.Input.Key.L)
            {
                Clear_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (ctrl && shift && e.Key == System.Windows.Input.Key.C)
            {
                // The checkbox is the single source of truth - CaptureTab reads it.
                CaseSensitiveCheck.IsChecked = CaseSensitiveCheck.IsChecked != true;
                e.Handled = true;
            }
            else if (ctrl && e.Key == System.Windows.Input.Key.T)
            {
                NewTab_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (ctrl && e.Key == System.Windows.Input.Key.W)
            {
                CloseActiveTab_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (ctrl && e.Key == System.Windows.Input.Key.Tab)
            {
                CycleTab(shift ? -1 : 1);   // Tabs.cs
                e.Handled = true;
            }
            else if (ctrl && e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
            {
                JumpToTab(e.Key - System.Windows.Input.Key.D1 + 1);   // Tabs.cs; 9 = last
                e.Handled = true;
            }
            else if (ctrl && e.Key >= System.Windows.Input.Key.NumPad1 && e.Key <= System.Windows.Input.Key.NumPad9)
            {
                JumpToTab(e.Key - System.Windows.Input.Key.NumPad1 + 1);
                e.Handled = true;
            }
            else if (ctrl && e.Key == System.Windows.Input.Key.N)
            {
                if (shift) AddFilter_Click(this, new RoutedEventArgs());
                else       _active.Groups[_active.Groups.Count - 1].Terms.Add(new SearchTerm());
                e.Handled = true;
            }
            else if (ctrl && e.Key == System.Windows.Input.Key.O)
            {
                OpenFolderPicker();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                // Let open dropdowns, the date box, and the filter box handle Enter
                // themselves, otherwise they can never commit.
                if (e.OriginalSource is ComboBoxItem ||
                    (e.OriginalSource is ComboBox cb && cb.IsDropDownOpen) ||
                    e.OriginalSource is System.Windows.Controls.Primitives.DatePickerTextBox ||
                    ReferenceEquals(e.OriginalSource, Pane.ResultFilterBox))
                    return;

                Search_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                // Smart Esc, in order: close the filter bar > close an open overlay >
                // stop a running search > offer to quit (with remember-my-choice).
                e.Handled = true;
                if (Pane.ResultFilterBar.Visibility == Visibility.Visible)
                    ResultFilterClose_Click(this, new RoutedEventArgs());
                else if (PatternHelpOverlay.Visibility == Visibility.Visible)
                    PatternHelpClose_Click(this, new RoutedEventArgs());
                else if (ShortcutsOverlay.Visibility == Visibility.Visible)
                    ShortcutsClose_Click(this, new RoutedEventArgs());
                else if (AboutOverlay.Visibility == Visibility.Visible)
                    AboutClose_Click(this, new RoutedEventArgs());
                else if (_active.IsSearching)
                    _active.Cts?.Cancel();
                else
                    RequestQuit();   // Session.cs
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  ENGINE CALLBACKS  (marshalled to the UI thread, routed per tab)
        // ═══════════════════════════════════════════════════════════
        private void SetTabStatus(SearchTab tab, string msg)
        {
            tab.StatusKey     = null;   // transient text - not re-renderable on language switch
            tab.StatusArgs    = null;
            tab.StatusMessage = msg;
            if (tab == _active) { StatusText.Text = msg; ApplyStatusTone(null); }
        }

        // The footer indicator light. Green normal, amber for something that did not happen but
        // was not a fault, red for a genuine failure.
        //
        // Driven off the status KEY rather than a separate argument at every call site: the key
        // already says which of those three a message is, and threading a tone through forty
        // SetTabStatusKey calls would be forty chances to get it wrong. A raw SetTabStatus has no
        // key and is always green - those are progress messages.
        private static readonly string[] WarnKeys =
        {
            "Str_Status_FileOnly", "Str_Status_ClipboardBusy", "Str_Status_ElevationDeclined",
        };

        private static readonly string[] ErrorKeys =
        {
            "Str_Status_BadPath", "Str_Status_ShellFailed",
        };

        private void ApplyStatusTone(string? key)
        {
            // A real traffic light: three fixed colors. This used to fall back to PrimaryBrush,
            // which meant "fine" rendered as whatever accent was picked - blue, red, whatever -
            // so the dot carried no information at all unless something had gone wrong.
            string brush = key != null && System.Array.IndexOf(ErrorKeys, key) >= 0 ? "DangerRed"
                         : key != null && System.Array.IndexOf(WarnKeys,  key) >= 0 ? "WarnBrush"
                         : "OkBrush";

            StatusDot.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brush);
        }

        // Key-based variant: stores the resource key + args on the tab so a live
        // language switch can re-render every tab's status line (RelocalizeDynamicUi).
        private void SetTabStatusKey(SearchTab tab, string key, params object[] args)
        {
            tab.StatusKey     = key;
            tab.StatusArgs    = args;
            tab.StatusMessage = args.Length > 0 ? string.Format(Loc(key), args) : Loc(key);
            if (tab == _active) { StatusText.Text = tab.StatusMessage; ApplyStatusTone(key); }
        }

        // All engine callbacks land at Background priority so queued result churn sits
        // behind input. Priority alone is NOT what keeps the window alive though: it only
        // orders work that is still queued, and a callback already running cannot be
        // interrupted. Responsiveness comes from the engine capping each batch (SearchEngine.cs
        // MaxBatch), so every callback is short and input gets a slot between them. Do not
        // remove that cap on the assumption this priority is covering it.
        private void OnResultsBatch(SearchTab tab, List<SearchResult> batch)
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var result in batch)
                {
                    tab.Results.Add(result);
                    foreach (var m in result.Matches)
                    {
                        if (m.Term.MatchCount < 0) m.Term.MatchCount = 0;
                        m.Term.MatchCount++;
                    }
                }
                int c = tab.Results.Count;
                tab.StatsLabel = c > 0 ? string.Format(Loc("Str_Count_Matches"), c.ToString("N0")) : string.Empty;
                if (tab == _active) StatsText.Text = tab.StatsLabel;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnStatusChanged(SearchTab tab, string status)
            => Dispatcher.InvokeAsync(() => SetTabStatus(tab, status),
                System.Windows.Threading.DispatcherPriority.Background);

        private void OnProgressChanged(SearchTab tab, int processed)
            => Dispatcher.InvokeAsync(() =>
            {
                tab.ScannedCount = processed;
                tab.ScannedLabel = string.Format(Loc("Str_Status_Scanned"), processed.ToString("N0"));
                if (tab == _active) ScannedText.Text = tab.ScannedLabel;
            }, System.Windows.Threading.DispatcherPriority.Background);

        private void SetStatus(string msg) => SetTabStatus(_active, msg);
    }
}
