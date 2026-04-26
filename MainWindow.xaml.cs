using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using KillerFind.Models;

namespace KillerFind
{
    public partial class MainWindow : Window
    {
        // ── Collections ──────────────────────────────────────────
        private readonly ObservableCollection<SearchTerm>   _terms   = [];
        private readonly ObservableCollection<SearchResult> _results = [];

        // ── Search state ─────────────────────────────────────────
        private readonly SearchEngine    _engine = new();
        private CancellationTokenSource? _cts;
        private bool                      _isSearching;

        // ── Construction ─────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            TermsList.ItemsSource   = _terms;
            ResultsList.ItemsSource = _results;

            _engine.ResultsBatch    += OnResultsBatch;
            _engine.StatusChanged   += OnStatusChanged;
            _engine.ProgressChanged += OnProgressChanged;

            // Start with one blank term
            _terms.Add(new SearchTerm());
        }

        // ═══════════════════════════════════════════════════════════
        //  SCOPE — folder picker
        // ═══════════════════════════════════════════════════════════
        private void ScopeBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => OpenFolderPicker();

        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
            => OpenFolderPicker();

        private void OpenFolderPicker()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description         = "Select root search folder",
                ShowNewFolderButton = false
            };
            if (!string.IsNullOrWhiteSpace(RootPathBox.Text) &&
                Directory.Exists(RootPathBox.Text))
                dlg.SelectedPath = RootPathBox.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RootPathBox.Text    = dlg.SelectedPath;
                ScopePathLabel.Text = dlg.SelectedPath;
            }
        }


        // ═══════════════════════════════════════════════════════════
        //  SEARCH TERMS
        // ═══════════════════════════════════════════════════════════
        private void AddTerm_Click(object sender, RoutedEventArgs e)
            => _terms.Add(new SearchTerm());

        private void RemoveTerm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTerm term && _terms.Count > 1)
                _terms.Remove(term);
        }

        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchTerm term)
                term.Mode = term.Mode == SearchTerm.SearchMode.FileName
                    ? SearchTerm.SearchMode.Content
                    : SearchTerm.SearchMode.FileName;
        }

        // ═══════════════════════════════════════════════════════════
        //  SEARCH / STOP
        // ═══════════════════════════════════════════════════════════
        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                _cts?.Cancel();
                return;
            }

            string root = RootPathBox.Text.Trim();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                SetStatus("Invalid or missing root path.");
                return;
            }

            var activeTerms = _terms.Where(t => !string.IsNullOrWhiteSpace(t.Pattern)).ToList();
            if (activeTerms.Count == 0)
            {
                SetStatus("Add at least one search term.");
                return;
            }

            // ── Reset UI ──────────────────────────────────────────
            _results.Clear();
            foreach (var t in _terms) t.ResetCount();
            ScannedText.Text       = string.Empty;
            ScannedText.Visibility = Visibility.Visible;
            StatsText.Text            = string.Empty;
            ResultsHeader.Text        = "RESULTS";

            _isSearching         = true;
            SearchButton.Content = "[ STOP ]";

            _cts = new CancellationTokenSource();
            var sw = Stopwatch.StartNew();

            try
            {
                await _engine.SearchAsync(
                    root,
                    activeTerms,
                    IncludePatternsBox.Text,
                    ExcludePatternsBox.Text,
                    CaseSensitiveCheck.IsChecked == true,
                    _cts.Token);

                sw.Stop();
                SetStatus($"Done in {sw.Elapsed.TotalSeconds:0.00}s  —  {_results.Count} file(s) matched.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped.");
            }
            finally
            {
                _isSearching              = false;
                SearchButton.Content      = "[ SEARCH ]";
                ScannedText.Visibility = Visibility.Collapsed;
                ResultsHeader.Text        = $"RESULTS  ({_results.Count})";
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _results.Clear();
            foreach (var t in _terms) t.ResetCount();
            ScannedText.Visibility = Visibility.Collapsed;
            StatsText.Text            = string.Empty;
            ResultsHeader.Text        = "RESULTS";
            SetStatus("Cleared.");
        }

        // ═══════════════════════════════════════════════════════════
        //  ENGINE CALLBACKS  (marshalled back to UI thread)
        // ═══════════════════════════════════════════════════════════
        private void OnResultsBatch(List<SearchResult> batch)
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var result in batch)
                {
                    _results.Add(result);
                    foreach (var m in result.Matches)
                    {
                        if (m.Term.MatchCount < 0) m.Term.MatchCount = 0;
                        m.Term.MatchCount++;
                    }
                }
                int c = _results.Count;
                StatsText.Text = c > 0 ? $"{c:N0} match{(c == 1 ? "" : "es")}" : string.Empty;
            });
        }

        private void OnStatusChanged(string status)
            => Dispatcher.InvokeAsync(() => SetStatus(status));

        private void OnProgressChanged(int processed)
            => Dispatcher.InvokeAsync(() => ScannedText.Text = $"{processed:N0} files scanned");


        // ═══════════════════════════════════════════════════════════
        //  RESULT CLICK — reveal file in Explorer
        // ═══════════════════════════════════════════════════════════
        private void ResultHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string path && File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            ResultsList.SelectedItem = null;
            e.Handled = true;
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string path && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string path && File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        // ═══════════════════════════════════════════════════════════
        //  HTML EXPORT
        // ═══════════════════════════════════════════════════════════
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                SetStatus("Nothing to export yet.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "HTML Files|*.html",
                FileName = $"KillerFind-{DateTime.Now:yyyyMMdd-HHmmss}.html",
                Title    = "Save results as HTML"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    new HtmlExporter().Export(dlg.FileName, _results, _terms, RootPathBox.Text);
                    SetStatus($"Exported: {dlg.FileName}");
                    Process.Start(dlg.FileName);
                }
                catch (Exception ex)
                {
                    SetStatus($"Export failed: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  CUSTOM WINDOW CHROME  (WindowStyle=None)
        // ═══════════════════════════════════════════════════════════
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        // ═══════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════
        private void SetStatus(string msg) => StatusText.Text = msg;

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
