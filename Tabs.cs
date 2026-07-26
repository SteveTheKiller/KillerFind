using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KillerFind.Models;

// Tab lifecycle + the KillerPDF-style tab strip physics. Partial of MainWindow.
// Each SearchTab is a complete search; the left panel and results pane always show
// the ACTIVE tab (ActivateTab points every ItemsSource/field at it).
namespace KillerFind
{
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════════════════════
        //  TAB LIFECYCLE
        // ═══════════════════════════════════════════════════════════
        private SearchTab CreateTab()
        {
            var tab = new SearchTab(Loc("Str_Tab_New"));
            var group = new TermGroup();
            group.Terms.Add(new SearchTerm());
            tab.Groups.Add(group);
            tab.StatusMessage = Loc("Str_Status_Ready");
            tab.StatusKey     = "Str_Status_Ready";

            // Each tab owns its engine; callbacks carry the tab so a background
            // tab's search never paints over the active tab's UI.
            tab.Engine.ResultsBatch    += batch => OnResultsBatch(tab, batch);
            tab.Engine.StatusChanged   += msg   => OnStatusChanged(tab, msg);
            tab.Engine.ProgressChanged += n     => OnProgressChanged(tab, n);

            _tabs.Add(tab);
            UpdateTabBar();
            return tab;
        }

        // The tab bar only exists once there are 2+ tabs (like KillerPDF). While it is
        // visible, each top corner squares off ONLY when the tab sitting on it is the active
        // one - first tab owns the top-left, last tab owns the top-right (the tabs fill the
        // strip edge to edge, so those two always reach the pane's corners). The active tab
        // is painted in PaneBrush, so it and the pane have to read as one surface: the strip
        // covers just the 1px the pane is pulled up by (its -1 top margin), and a 6px radius
        // under it leaves ~5px of curve - plus the border stroke along it - showing as a
        // rounded step and a tab-shaped outline. An inactive tab is window-colored, so it is
        // a different surface anyway and the pane keeps its rounding under it. One tab
        // collapses the strip and the pane takes its fully rounded top back. Re-run on tab
        // switch and after a drag-reorder, since either can change which tab owns a corner.
        // Runs for EVERY live pane, not just the focused one: the band takes real height, so a
        // pane that shows it sits lower than a pane that does not, and the two pane tops stopped
        // lining up the moment their tab counts differed.
        private void UpdateTabBar()
        {
            ForEachPane(UpdateTabBarInPane);   // Panes.cs

            // The pane's top margin depends on whether the strip is showing, which is exactly
            // what just changed (DualPane.cs).
            ApplyPaneMargins();

            // Which tab owns the strip's left and right edge can change on any add, close or
            // drag-reorder, and the ring's verticals follow it too.
            UpdatePaneFocusRing();
        }

        private void UpdateTabBarInPane()
        {
            // With two panes open the band shows in BOTH as soon as EITHER has more than one
            // tab. A single-tab pane then shows its one tab, which is still a thing you can
            // click; reserving blank strip height instead would line the tops up with dead
            // space. With one pane LivePanes() yields only that pane, so this is the old
            // "2+ tabs" rule unchanged.
            bool show = LivePanes().Any(p => p.Tabs.Count > 1);
            Pane.TabBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            // Which tab is rightmost drives its divider (see SearchTab.IsLast).
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].IsLast = i == _tabs.Count - 1;

            if (show)
            {
                bool firstActive = _tabs.Count > 0 && _tabs[0] == _active;
                bool lastActive  = _tabs.Count > 0 && _tabs[_tabs.Count - 1] == _active;
                Pane.ResultsPane.CornerRadius = new CornerRadius(firstActive ? 0 : 6, lastActive ? 0 : 6, 6, 6);
                Pane.ScopeBar.CornerRadius    = new CornerRadius(firstActive ? 0 : 5, 0, 0, 0);
                // The ring line in the band IS the pane's top border, so it curves where the
                // pane curves. Left flat and full-width it overshot the corner and read as a
                // rule laid across the pane rather than as its edge (FilePane.xaml).
                Pane.TabBarRing.CornerRadius  = new CornerRadius(firstActive ? 0 : 6, lastActive ? 0 : 6, 0, 0);
            }
            else
            {
                Pane.ResultsPane.CornerRadius = new CornerRadius(6);
                Pane.ScopeBar.CornerRadius    = new CornerRadius(5, 0, 0, 0);
            }
        }

        // Save the left panel's editable fields into the outgoing tab.
        private void CaptureTab(SearchTab t)
        {
            t.RootPath        = Pane.RootPathBox.Text;
            t.IncludePatterns = IncludePatternsBox.Text;
            t.ExcludePatterns = ExcludePatternsBox.Text;
            t.CaseSensitive   = CaseSensitiveCheck.IsChecked == true;
        }

        // Point the whole UI at a tab: collections, config boxes, status, counters, button label.
        private void ActivateTab(SearchTab t)
        {
            _active = t;
            foreach (var tab in _tabs) tab.IsActive = tab == t;
            UpdatePaneFocusRing();   // DualPane.cs - the ring follows the ACTIVE tab, so
                                     // switching tabs has to move it, not just switching panes

            TermsList.ItemsSource   = t.Groups;
            FiltersList.ItemsSource = t.Filters;
            Pane.ResultsList.ItemsSource = t.Results;

            ApplyTerminalView(t);   // TerminalTabs.cs - a shell tab shows a pty, not a listing
            ApplyEditorView(t);     // EditorTabs.cs   - and a document tab shows a document
            ApplyPaneBars(t);       // PaneBars.cs     - each of the three wears its own bar

            Pane.RootPathBox.Text             = t.RootPath;
            Pane.ScopePathLabel.Text          = t.PipeFiles != null ? t.PipeLabel
                : string.IsNullOrEmpty(t.RootPath) ? Loc("Str_Scope_Empty") : t.RootPath;
            IncludePatternsBox.Text      = t.IncludePatterns;
            ExcludePatternsBox.Text      = t.ExcludePatterns;
            CaseSensitiveCheck.IsChecked = t.CaseSensitive;

            SetFooterStatus(t.StatusMessage);           // window footer - the live line
            // The light belongs to whichever tab is showing: switching from a running tab to an
            // idle one has to drop it back off amber, and vice versa.
            ApplyStatusTone(t.StatusKey);
            Pane.QueryText.Text    = t.QueryLabel;
            SetExpandAllLabel(t.Results.Count > 0 && t.Results.All(r => r.IsExpanded));
            ApplySort(t);
            Pane.ResultFilterBox.Text     = t.FilterText;
            Pane.ResultFilterBar.Visibility = t.FilterText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter(t);
            Pane.ScannedText.Text       = t.ScannedLabel;
            Pane.ScannedText.Visibility = t.IsSearching ? Visibility.Visible : Visibility.Collapsed;
            Pane.StatsText.Text         = t.StatsLabel;
            UpdatePaneStatusBar();      // the incoming tab may have nothing to report
            UpdateLocationColumn();   // ViewOptions.cs - a browsing tab hides the folder column
            SearchButton.Content   = t.IsSearching ? Loc("Str_Btn_Stop") : Loc("Str_Btn_Search");
            Pane.ResultsHeader.Text = t.Results.Count > 0
                ? string.Format(Loc("Str_Lbl_ResultsCount"), t.Results.Count)
                : Loc("Str_Lbl_Results");
            UpdateNavButtons();   // Browse.cs - back/forward/up belong to the incoming tab

            // Only the active tab is watched (BrowseWatcher.cs), so the watch moves with it and
            // the incoming tab gets a silent refresh to catch anything that changed on disk while
            // it sat in the background.
            StopWatching();
            if (t.IsBrowsing && System.IO.Directory.Exists(t.CurrentFolder))
                _ = NavigateTo(t.CurrentFolder, record: false);

            UpdateTabBar();       // corner rounding follows which tab is active
        }

        private void SwitchToTab(SearchTab t)
        {
            if (t == _active) return;
            // Deliberately instant: blending two result lists reads as flicker.
            CaptureTab(_active);
            ActivateTab(t);
        }

        // ── Pane crossfade (tab CLOSE only) ────────────────────────
        // Closing the active tab yanks its content away, so a short ghost fade
        // softens it. Plain tab switches are instant by design.

        private System.Windows.Media.ImageSource? SnapshotPane()
        {
            if (Pane.ResultsPane.ActualWidth < 1 || Pane.ResultsPane.ActualHeight < 1) return null;
            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(Pane.ResultsPane.ActualWidth  * dpi.DpiScaleX),
                    (int)Math.Ceiling(Pane.ResultsPane.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(Pane.ResultsPane);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }   // cosmetic only - the close still happens
        }

        private void RunPaneCrossfade(System.Windows.Media.ImageSource? snap)
        {
            if (snap == null) return;
            Pane.TabFadeGhost.BeginAnimation(OpacityProperty, null);
            Pane.TabFadeGhost.Source     = snap;
            Pane.TabFadeGhost.Opacity    = 1;
            Pane.TabFadeGhost.Visibility = Visibility.Visible;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            fade.Completed += (_, _) =>
            {
                Pane.TabFadeGhost.BeginAnimation(OpacityProperty, null);
                Pane.TabFadeGhost.Visibility = Visibility.Collapsed;
                Pane.TabFadeGhost.Source     = null;
            };
            Pane.TabFadeGhost.BeginAnimation(OpacityProperty, fade);
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            CaptureTab(_active);
            ActivateTab(CreateTab());

            // A tab is a place now, not a blank search form. Opening at Home means the location
            // row is never empty and the results pane never starts as a prompt (AddressBar.cs).
            _ = NavigateTo(HomeFolder);   // Browse.cs
        }

        internal void Tab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Left-click switching happens on mouse-UP (Tab_DragUp) so a press can
            // begin a drag without switching first - the KillerPDF tab physics.
            if (sender is not FrameworkElement fe || fe.DataContext is not SearchTab t) return;
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) { CloseTab(t); e.Handled = true; }
        }

        internal void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTab t) CloseTab(t);
        }

        private void CloseActiveTab_Click(object sender, RoutedEventArgs e) => CloseTab(_active);

        private void CloseTab(SearchTab t)
        {
            // The one close that can destroy something: unsaved typing exists nowhere else, so
            // a document tab gets asked first (EditorTabs.cs). Ahead of the fade, because the
            // dialog has to be able to call the whole thing off.
            if (!ConfirmDiscard(t)) return;

            t.Cts?.Cancel();   // stop its search; the engine winds down gracefully

            // Fade the tab CHIP out first (when the bar is visible), then remove.
            var cont = TabContainer(t);
            if (cont != null && _tabs.Count > 1)
            {
                cont.IsHitTestVisible = false;   // no clicks on a dying tab
                var chipFade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(110))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                        { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                };
                chipFade.Completed += (_, _) => FinishCloseTab(t);
                cont.BeginAnimation(OpacityProperty, chipFade);
                return;
            }
            FinishCloseTab(t);
        }

        private void FinishCloseTab(SearchTab t)
        {
            if (!_tabs.Contains(t)) return;   // guard against a double-fire

            CloseTerminal(t);   // TerminalTabs.cs - a shell tab has a pty to end
            CloseEditor(t);     // EditorTabs.cs   - a document tab has a control to unhook

            // Only closing the ACTIVE tab changes what the pane shows - fade that.
            var snap = t == _active ? SnapshotPane() : null;

            int idx = _tabs.IndexOf(t);
            _tabs.Remove(t);

            if (_tabs.Count == 0)
            {
                // Closing the LAST tab of the second pane closes the pane. A pane with nothing
                // in it is not a thing you can be looking at, and handing back a fresh blank tab
                // instead left the only way out of dual pane being to go and find the toggle.
                // The first pane keeps the always-one-tab rule - there is nothing to fall back
                // to there. CloseSecondPane moves focus to the survivor before it hides this one.
                if (DualPane && ReferenceEquals(Pane, RightPane))
                {
                    CloseSecondPane();   // DualPane.cs
                    return;
                }

                ActivateTab(CreateTab());
                RunPaneCrossfade(snap);
                return;
            }
            if (t == _active)
            {
                ActivateTab(_tabs[Math.Min(idx, _tabs.Count - 1)]);
                RunPaneCrossfade(snap);
            }
            UpdateTabBar();
        }

        // ── Keyboard tab navigation (Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+1-9) ──
        private void CycleTab(int dir)
        {
            if (_tabs.Count < 2) return;
            int idx = (_tabs.IndexOf(_active) + dir + _tabs.Count) % _tabs.Count;
            SwitchToTab(_tabs[idx]);
        }

        private void JumpToTab(int oneBased)
        {
            if (_tabs.Count == 0) return;
            int idx = oneBased >= 9 ? _tabs.Count - 1 : oneBased - 1;   // Ctrl+9 = last, browser-style
            if (idx >= 0 && idx < _tabs.Count) SwitchToTab(_tabs[idx]);
        }

        // ═══════════════════════════════════════════════════════════
        //  TAB STRIP PHYSICS (ported from KillerPDF Tabs.cs, adapted to the
        //  ItemsControl strip): arm on press; past the threshold the grabbed tab
        //  glues to the cursor and neighbors glide aside as it crosses their
        //  layout-slot midpoints. A plain click still switches on release.
        // ═══════════════════════════════════════════════════════════
        private SearchTab? _tabDragTab;
        private Point  _tabDragStart;
        private double _tabGrabDX;
        private bool   _tabDragging;

        private FrameworkElement? TabContainer(SearchTab t)
            => Pane.TabStrip.ItemContainerGenerator.ContainerFromItem(t) as FrameworkElement;

        private static bool InsideButton(object src)
        {
            var d = src as DependencyObject;
            while (d != null && d is not Button && d is not Window)
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d is Button;
        }

        private static double LayoutMidX(FrameworkElement fe)
        {
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(fe);
            return slot.X + slot.Width / 2;
        }

        private static void SetTabOffsetX(FrameworkElement tab, double x)
        {
            if (tab.RenderTransform is not System.Windows.Media.TranslateTransform tt)
            {
                tt = new System.Windows.Media.TranslateTransform();
                tab.RenderTransform = tt;
            }
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            tt.X = x;
        }

        private static void AnimateTabSlide(FrameworkElement? tab, double fromX)
        {
            if (tab == null) return;
            if (tab.RenderTransform is not System.Windows.Media.TranslateTransform tt)
            {
                tt = new System.Windows.Media.TranslateTransform();
                tab.RenderTransform = tt;
            }
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            var anim = new System.Windows.Media.Animation.DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
        }

        internal void Tab_DragDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement bd || bd.DataContext is not SearchTab t) return;
            if (InsideButton(e.OriginalSource)) return;   // the close x handles its own click
            _tabDragTab   = t;
            _tabDragStart = e.GetPosition(Pane.TabStrip);
            _tabGrabDX    = e.GetPosition(bd).X;
            _tabDragging  = false;
            bd.CaptureMouse();
            e.Handled = true;
        }

        internal void Tab_DragMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not FrameworkElement bd || !bd.IsMouseCaptured || _tabDragTab is null) return;
            var cont = TabContainer(_tabDragTab);
            if (cont == null) return;

            double x = e.GetPosition(Pane.TabStrip).X;
            if (!_tabDragging && Math.Abs(x - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _tabDragging = true;
            Panel.SetZIndex(cont, 3);   // grabbed tab rides above its neighbors

            // Over the other pane the real tab cannot follow the hand - it is still parked in
            // the strip it came from - so a ghost takes over and the reorder stands down
            // (PaneDrag.cs). Coming back into this pane hands control straight back.
            var over = DropTargetPane(e);
            UpdateDragFeedback(_tabDragTab, e, over);
            if (over != null) return;

            int cur = _tabs.IndexOf(_tabDragTab);
            double slide   = cont.ActualWidth + 1;               // +1 = tab margin gap
            double rawLeft = x - _tabGrabDX;
            double leftEdge  = rawLeft;
            double rightEdge = rawLeft + cont.ActualWidth;
            double maxLeft = Math.Max(0, Pane.TabStrip.ActualWidth - slide);
            double renderLeft = Math.Min(Math.Max(0, rawLeft), maxLeft);

            // Swap when the ADVANCING edge crosses a neighbor's layout-slot midpoint
            // (edge-vs-midpoint gives natural hysteresis, no bounce).
            bool swapped = false;
            if (cur + 1 < _tabs.Count && TabContainer(_tabs[cur + 1]) is { } right && rightEdge > LayoutMidX(right))
            {
                _tabs.Move(cur + 1, cur);
                AnimateTabSlide(TabContainer(_tabs[cur]), slide);    // it jumped left; glide it in from the right
                swapped = true;
            }
            else if (cur - 1 >= 0 && TabContainer(_tabs[cur - 1]) is { } left && leftEdge < LayoutMidX(left))
            {
                _tabs.Move(cur - 1, cur);
                AnimateTabSlide(TabContainer(_tabs[cur]), -slide);   // it jumped right; glide it in from the left
                swapped = true;
            }

            if (swapped) Pane.TabStrip.UpdateLayout();
            var dragged = TabContainer(_tabDragTab);
            if (dragged == null) return;
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(dragged);
            SetTabOffsetX(dragged, renderLeft - slot.X);
        }

        internal void Tab_DragUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement bd || !bd.IsMouseCaptured) return;
            bd.ReleaseMouseCapture();
            bool wasDragging = _tabDragging;
            var  t = _tabDragTab;
            _tabDragTab  = null;
            _tabDragging = false;
            HideDragFeedback();   // PaneDrag.cs - the ghost goes whatever the drop turns out to be

            if (!wasDragging)
            {
                if (t != null) SwitchToTab(t);
                return;
            }

            // Dropped over the OTHER pane? Then this was a move, not a reorder (PaneDrag.cs).
            // Checked on release rather than mid-drag on purpose: moving a tab between panes
            // rebuilds its container, which would pull the mouse capture out from under the
            // drag that is still running.
            if (t != null && DropTargetPane(e) is { } target)
            {
                MoveTabToPane(t, target, e);
                return;
            }

            UpdateTabBar();   // a reorder may have moved the active tab on/off the corner

            // Settle the grabbed tab from its dragged offset into its final slot.
            var cont = t != null ? TabContainer(t) : null;
            if (cont?.RenderTransform is System.Windows.Media.TranslateTransform tt && Math.Abs(tt.X) > 0.5)
            {
                var settle = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                        { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                };
                settle.Completed += (_, _) => CleanupTabTransforms();
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, settle);
            }
            else CleanupTabTransforms();
        }

        private void CleanupTabTransforms()
        {
            foreach (var tab in _tabs)
                if (TabContainer(tab) is { } c)
                {
                    c.RenderTransform = null;
                    Panel.SetZIndex(c, 0);
                }
        }
    }
}
