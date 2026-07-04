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
        // visible the pane keeps a slight top rounding EXCEPT under the selected tab:
        // when the ACTIVE tab is the first one it sits flush on the pane's top-left
        // corner, so that corner squares off browser-style. Re-run on tab switch and
        // after a drag-reorder, since either can change which tab owns the corner.
        private void UpdateTabBar()
        {
            bool show = _tabs.Count > 1;
            TabBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show)
            {
                bool firstActive = _tabs.Count > 0 && _tabs[0] == _active;
                ResultsPane.CornerRadius = new CornerRadius(firstActive ? 0 : 6, 6, 6, 6);
                ScopeBar.CornerRadius    = new CornerRadius(firstActive ? 0 : 5, 0, 0, 0);
            }
            else
            {
                ResultsPane.CornerRadius = new CornerRadius(6);
                ScopeBar.CornerRadius    = new CornerRadius(5, 0, 0, 0);
            }
        }

        // Save the left panel's editable fields into the outgoing tab.
        private void CaptureTab(SearchTab t)
        {
            t.RootPath        = RootPathBox.Text;
            t.IncludePatterns = IncludePatternsBox.Text;
            t.ExcludePatterns = ExcludePatternsBox.Text;
            t.CaseSensitive   = CaseSensitiveCheck.IsChecked == true;
        }

        // Point the whole UI at a tab: collections, config boxes, status, counters, button label.
        private void ActivateTab(SearchTab t)
        {
            _active = t;
            foreach (var tab in _tabs) tab.IsActive = tab == t;

            TermsList.ItemsSource   = t.Groups;
            FiltersList.ItemsSource = t.Filters;
            ResultsList.ItemsSource = t.Results;

            RootPathBox.Text             = t.RootPath;
            ScopePathLabel.Text          = t.PipeFiles != null ? t.PipeLabel
                : string.IsNullOrEmpty(t.RootPath) ? Loc("Str_Scope_Empty") : t.RootPath;
            IncludePatternsBox.Text      = t.IncludePatterns;
            ExcludePatternsBox.Text      = t.ExcludePatterns;
            CaseSensitiveCheck.IsChecked = t.CaseSensitive;

            StatusText.Text        = t.StatusMessage;
            QueryText.Text         = t.QueryLabel;
            SetExpandAllLabel(t.Results.Count > 0 && t.Results.All(r => r.IsExpanded));
            _syncingSort = true;
            SortCombo.SelectedIndex = t.SortIndex;
            _syncingSort = false;
            ApplySort(t);
            ResultFilterBox.Text     = t.FilterText;
            ResultFilterBar.Visibility = t.FilterText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter(t);
            ScannedText.Text       = t.ScannedLabel;
            ScannedText.Visibility = t.IsSearching ? Visibility.Visible : Visibility.Collapsed;
            StatsText.Text         = t.StatsLabel;
            SearchButton.Content   = t.IsSearching ? Loc("Str_Btn_Stop") : Loc("Str_Btn_Search");
            ResultsHeader.Text     = t.Results.Count > 0
                ? string.Format(Loc("Str_Lbl_ResultsCount"), t.Results.Count)
                : Loc("Str_Lbl_Results");
            UpdateTabBar();   // corner rounding follows which tab is active
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
            if (ResultsPane.ActualWidth < 1 || ResultsPane.ActualHeight < 1) return null;
            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(ResultsPane.ActualWidth  * dpi.DpiScaleX),
                    (int)Math.Ceiling(ResultsPane.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(ResultsPane);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }   // cosmetic only - the close still happens
        }

        private void RunPaneCrossfade(System.Windows.Media.ImageSource? snap)
        {
            if (snap == null) return;
            TabFadeGhost.BeginAnimation(OpacityProperty, null);
            TabFadeGhost.Source     = snap;
            TabFadeGhost.Opacity    = 1;
            TabFadeGhost.Visibility = Visibility.Visible;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            fade.Completed += (_, _) =>
            {
                TabFadeGhost.BeginAnimation(OpacityProperty, null);
                TabFadeGhost.Visibility = Visibility.Collapsed;
                TabFadeGhost.Source     = null;
            };
            TabFadeGhost.BeginAnimation(OpacityProperty, fade);
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            CaptureTab(_active);
            ActivateTab(CreateTab());
        }

        private void Tab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Left-click switching happens on mouse-UP (Tab_DragUp) so a press can
            // begin a drag without switching first - the KillerPDF tab physics.
            if (sender is not FrameworkElement fe || fe.DataContext is not SearchTab t) return;
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) { CloseTab(t); e.Handled = true; }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTab t) CloseTab(t);
        }

        private void CloseActiveTab_Click(object sender, RoutedEventArgs e) => CloseTab(_active);

        private void CloseTab(SearchTab t)
        {
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

            // Only closing the ACTIVE tab changes what the pane shows - fade that.
            var snap = t == _active ? SnapshotPane() : null;

            int idx = _tabs.IndexOf(t);
            _tabs.Remove(t);

            if (_tabs.Count == 0)
            {
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
            => TabStrip.ItemContainerGenerator.ContainerFromItem(t) as FrameworkElement;

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

        private void Tab_DragDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement bd || bd.DataContext is not SearchTab t) return;
            if (InsideButton(e.OriginalSource)) return;   // the close x handles its own click
            _tabDragTab   = t;
            _tabDragStart = e.GetPosition(TabStrip);
            _tabGrabDX    = e.GetPosition(bd).X;
            _tabDragging  = false;
            bd.CaptureMouse();
            e.Handled = true;
        }

        private void Tab_DragMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not FrameworkElement bd || !bd.IsMouseCaptured || _tabDragTab is null) return;
            var cont = TabContainer(_tabDragTab);
            if (cont == null) return;

            double x = e.GetPosition(TabStrip).X;
            if (!_tabDragging && Math.Abs(x - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _tabDragging = true;
            Panel.SetZIndex(cont, 3);   // grabbed tab rides above its neighbors

            int cur = _tabs.IndexOf(_tabDragTab);
            double slide   = cont.ActualWidth + 1;               // +1 = tab margin gap
            double rawLeft = x - _tabGrabDX;
            double leftEdge  = rawLeft;
            double rightEdge = rawLeft + cont.ActualWidth;
            double maxLeft = Math.Max(0, TabStrip.ActualWidth - slide);
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

            if (swapped) TabStrip.UpdateLayout();
            var dragged = TabContainer(_tabDragTab);
            if (dragged == null) return;
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(dragged);
            SetTabOffsetX(dragged, renderLeft - slot.X);
        }

        private void Tab_DragUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement bd || !bd.IsMouseCaptured) return;
            bd.ReleaseMouseCapture();
            bool wasDragging = _tabDragging;
            var  t = _tabDragTab;
            _tabDragTab  = null;
            _tabDragging = false;

            if (!wasDragging)
            {
                if (t != null) SwitchToTab(t);
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
