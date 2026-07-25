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

            TabStrip.ItemsSource = _tabs;
            if (DemoMode || !TryRestoreTabs()) ActivateTab(CreateTab());   // Session.cs / Tabs.cs

            Loaded += (_, _) =>
            {
                // Demo mode: no install badge (and fabricated tabs, DemoMode.cs).
                if (App.IsPortable() && !DemoMode) PortableBadge.Visibility = Visibility.Visible;
                if (DemoMode) GenerateDemoData();
            };
            Closing += (_, _) => { if (!DemoMode) SaveTabsOnExit(); };     // Session.cs

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
        private void ScopeBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => OpenFolderPicker();

        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
            => OpenFolderPicker();

        private void OpenFolderPicker()
        {
            var dlg = new FolderPickerDialog(RootPathBox.Text) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string? picked = dlg.SelectedPath;
            if (picked is null || picked.Length == 0) return;

            RootPathBox.Text    = picked;
            ScopePathLabel.Text = picked;
            _active.RootPath    = picked;
            _active.Title       = ToTabTitle(picked);
            // Picking a folder is the escape hatch from a piped scope.
            _active.PipeFiles   = null;
            _active.PipeLabel   = string.Empty;
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

        // Global keys: Enter runs the search, Esc closes the filter bar or stops a
        // running search, Ctrl+F opens the results quick-filter.
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var mods  = System.Windows.Input.Keyboard.Modifiers;
            bool ctrl  = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
            bool shift = (mods & System.Windows.Input.ModifierKeys.Shift)   != 0;

            if (e.Key == System.Windows.Input.Key.F && ctrl && !shift)
            {
                ShowResultFilterBar();   // Results.cs
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F1)
            {
                // F1: the patterns + shortcuts card.
                PatternHelp_Click(this, new RoutedEventArgs());
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
                    ReferenceEquals(e.OriginalSource, ResultFilterBox))
                    return;

                Search_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                // Smart Esc, in order: close the filter bar > close an open overlay >
                // stop a running search > offer to quit (with remember-my-choice).
                e.Handled = true;
                if (ResultFilterBar.Visibility == Visibility.Visible)
                    ResultFilterClose_Click(this, new RoutedEventArgs());
                else if (PatternHelpOverlay.Visibility == Visibility.Visible)
                    PatternHelpClose_Click(this, new RoutedEventArgs());
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
            if (tab == _active) StatusText.Text = msg;
        }

        // Key-based variant: stores the resource key + args on the tab so a live
        // language switch can re-render every tab's status line (RelocalizeDynamicUi).
        private void SetTabStatusKey(SearchTab tab, string key, params object[] args)
        {
            tab.StatusKey     = key;
            tab.StatusArgs    = args;
            tab.StatusMessage = args.Length > 0 ? string.Format(Loc(key), args) : Loc(key);
            if (tab == _active) StatusText.Text = tab.StatusMessage;
        }

        // All engine callbacks land at Background priority: scrolling and typing
        // (input priority) always win over result churn, so the window stays
        // responsive mid-search even when batches are huge.
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
