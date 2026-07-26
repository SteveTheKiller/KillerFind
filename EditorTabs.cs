using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using KillerFind.Models;

// Editor tabs: opening a file for editing, and where it lands. Partial of MainWindow.
//
// ONE SEAM, deliberately. Everything that wants a file edited - the prompt script, the
// PowerShell profile, the results menu - goes through OpenForEditing, so there is a single
// method that decides what "edit" means and no call site holds an opinion about it.
//
// The placement rule is the opposite of the shell's (TerminalTabs.cs). A shell always opens in
// the SECOND pane because it is a companion to whatever you are looking at; a document IS what
// you are looking at, so it opens in the pane you asked from. Asking twice for the same file
// opens nothing new: the tab already holding it comes forward, because two editors over one
// file is a way to lose work rather than a feature.
namespace KillerFind
{
    public partial class MainWindow
    {
        // A BOM on a NEW file, for the same reason PromptScript.cs writes one: PowerShell 5.1
        // reads a BOM-less file as the system ANSI codepage, so every box-drawing glyph in a
        // script written without one comes back as mojibake. Only these extensions, because
        // everywhere else a BOM is noise other tools have to step over. An EXISTING file keeps
        // whatever it already had - nothing here ever adds one (EditorControl.Detect).
        private static readonly string[] BomExtensions = { ".ps1", ".psm1", ".psd1" };

        // E70F, the pencil, so a document tab is not mistaken for a folder or a shell.
        private static readonly string GlyphEdit = ((char)0xE70F).ToString();

        /// <summary>
        /// Biggest file the editor will open.
        /// </summary>
        /// <remarks>
        /// AvalonEdit holds the whole document in memory and builds five balanced trees over it,
        /// so a log the size of a DVD does not open slowly - it locks the window while it tries.
        /// An Edit row that refuses out loud beats one that freezes the app, and 32 MB is far
        /// past anything anybody edits by hand.
        /// </remarks>
        private const long MaxEditBytes = 32L * 1024 * 1024;

        // ═══════════════════════════════════════════════════════════
        //  OPEN
        // ═══════════════════════════════════════════════════════════
        /// <summary>
        /// Open <paramref name="path"/> in an editor tab, creating the file if it is not there.
        /// </summary>
        /// <remarks>
        /// Created rather than refused, because the file that turns out to be missing is usually
        /// $PROFILE: on a machine nobody has customized PowerShell on it does not exist at all,
        /// and an edit row that does nothing on a fresh machine has failed at the one moment it
        /// was wanted.
        /// </remarks>
        internal void OpenForEditing(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);

                if (!File.Exists(path))
                {
                    bool bom = Array.IndexOf(BomExtensions,
                                             Path.GetExtension(path).ToLowerInvariant()) >= 0;
                    File.WriteAllText(path, string.Empty, new UTF8Encoding(bom));
                }

                long size = new FileInfo(path).Length;
                if (size > MaxEditBytes)
                {
                    SetTabStatusKey(_active, "Str_Ed_TooBig", Path.GetFileName(path),
                                    (size / 1024d / 1024d).ToString("N0"));
                    return;
                }
            }
            catch (Exception ex)
            {
                SetTabStatusKey(_active, "Str_Ed_OpenFailed", Path.GetFileName(path), ex.Message);
                return;
            }

            // Already open? Go there. Two views over one file is how an edit gets overwritten by
            // the other copy's save, and nothing about the second tab would say so.
            foreach (var pane in LivePanes())            // Panes.cs
                foreach (var open in pane.Tabs)
                    if (open.Editor != null &&
                        string.Equals(open.Editor.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        FocusPane(pane);                 // Panes.cs
                        SwitchToTab(open);               // Tabs.cs
                        return;
                    }

            CaptureTab(_active);                         // Tabs.cs - the outgoing tab keeps its state
            var tab = CreateEditorTab(path);
            if (tab != null) ActivateTab(tab);
        }

        // ═══════════════════════════════════════════════════════════
        //  BUILD
        // ═══════════════════════════════════════════════════════════
        private SearchTab? CreateEditorTab(string path)
        {
            // Loaded BEFORE the tab is registered, so a file that cannot be read leaves no
            // half-built tab behind to close.
            var editor = new Editing.EditorControl(path);
            if (!editor.LoadFile(out string error))
            {
                SetTabStatusKey(_active, "Str_Ed_OpenFailed", Path.GetFileName(path), error);
                return null;
            }

            var tab = CreateTab();                       // Tabs.cs - registers it in this pane
            tab.Editor     = editor;
            tab.TabGlyph   = GlyphEdit;
            tab.IsBrowsing = false;

            // The address row reads the file's FOLDER rather than "no folder selected", and the
            // nav buttons stay meaningful, exactly as they do on a shell tab.
            tab.CurrentFolder = Path.GetDirectoryName(path) ?? string.Empty;
            tab.RootPath      = tab.CurrentFolder;

            SetEditorTitle(tab);
            editor.DirtyChanged += () => { SetEditorTitle(tab); SyncEditorBar(tab); };

            // Ctrl+wheel moves the app-wide size, so the other open documents and both bars have
            // to follow it (Editing/EditorControl.OnPreviewMouseWheel).
            editor.ZoomChanged += ApplyEditorOptions;

            // Menu rows the control cannot carry out itself, because they are about the tab, the
            // bar or the settings rather than about the text (Editing/EditorMenu.cs).
            editor.MenuCommand += cmd =>
            {
                switch (cmd)
                {
                    case Editing.EditorMenuCommand.GoToLine:
                        EditorGoto_Click(this, new RoutedEventArgs());     // EditorBar.cs
                        break;
                    case Editing.EditorMenuCommand.ToggleWrap:
                        EditorWrap_Click(this, new RoutedEventArgs());
                        break;
                    case Editing.EditorMenuCommand.Save:
                        SaveActiveEditor();
                        break;
                    case Editing.EditorMenuCommand.Settings:
                        EditorGear_Click(this, new RoutedEventArgs());
                        break;
                    case Editing.EditorMenuCommand.CloseTab:
                        CloseTab(tab);                                     // Tabs.cs
                        break;
                }
            };
            return tab;
        }

        /// <summary>The file name, with a dot in front while there are unsaved changes.</summary>
        /// <remarks>
        /// A dot rather than the usual asterisk. The tab already carries a glyph on its left and
        /// a close x on its right, and at this size an asterisk reads as part of the file name
        /// instead of as a mark on it.
        /// </remarks>
        private static void SetEditorTitle(SearchTab t)
        {
            if (t.Editor == null) return;
            string name = Path.GetFileName(t.Editor.FilePath);
            t.Title = t.Editor.Dirty ? ((char)0x2022) + " " + name : name;
        }

        // ═══════════════════════════════════════════════════════════
        //  ACTIVATION
        // ═══════════════════════════════════════════════════════════
        /// <summary>
        /// Swap the pane between its listing and a document. Called from ActivateTab, so it runs
        /// on every tab switch in either pane.
        /// </summary>
        /// <remarks>
        /// Runs AFTER ApplyTerminalView and quietly re-makes two of its decisions. That is
        /// deliberate: the terminal path has a live pty on the other end of it, and leaving it
        /// to reach its own conclusions untouched is worth one redundant assignment here.
        /// </remarks>
        private void ApplyEditorView(SearchTab t)
        {
            bool editing = t.Editor != null;

            Pane.EditorHost.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

            // MOVED rather than rebuilt, for the same reason the terminal is: the control holds
            // the document, the undo stack and the caret, and a fresh one per activation would
            // throw away all three on every tab switch.
            Pane.EditorSlot.Content = editing ? t.Editor : null;
            if (!editing) return;

            Pane.ResultsList.Visibility = Visibility.Collapsed;
            ApplyPaneToolbarMode(true);   // TerminalTabs.cs - sorting a document means nothing
            SyncEditorBar(t);             // EditorBar.cs - the strip belongs to the pane, so it
                                          // has to be repointed at the incoming document

            var editor = t.Editor!;
            // Focus has to wait for the swap to lay out, or it lands on an element that is still
            // collapsed and silently does nothing.
            Dispatcher.BeginInvoke(new Action(() => editor.TextArea.Focus()),
                                   System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>Tear a document down when its tab closes. Called from FinishCloseTab.</summary>
        private void CloseEditor(SearchTab t)
        {
            if (t.Editor == null) return;
            if (ReferenceEquals(Pane.EditorSlot.Content, t.Editor)) Pane.EditorSlot.Content = null;
            t.Editor = null;
        }

        // ═══════════════════════════════════════════════════════════
        //  SAVE
        // ═══════════════════════════════════════════════════════════
        /// <summary>Ctrl+S. A no-op on any tab that is not a document.</summary>
        private void SaveActiveEditor()
        {
            var t = _active;
            if (t.Editor == null) return;

            string name = Path.GetFileName(t.Editor.FilePath);
            if (t.Editor.SaveFile(out string error)) SetTabStatusKey(t, "Str_Ed_Saved", name);
            else                                     SetTabStatusKey(t, "Str_Ed_SaveFailed", name, error);

            SetEditorTitle(t);
            SyncEditorBar(t);   // EditorBar.cs - the save button drops out of the accent
        }

        /// <summary>Ask before throwing away unsaved changes. True to go ahead with the close.</summary>
        /// <remarks>
        /// The only modal question in the tab lifecycle, and it earns one: every other close
        /// throws away a search that can be re-run in a second, while this one throws away
        /// typing that exists nowhere else.
        /// </remarks>
        private bool ConfirmDiscard(SearchTab t)
        {
            if (t.Editor == null || !t.Editor.Dirty) return true;

            var dlg = new ConfirmDialog(Loc("Str_Dlg_DiscardMsg"), t.Editor.FilePath,
                                        Loc("Str_Btn_Discard")) { Owner = this };
            dlg.ShowDialog();
            return dlg.Confirmed;
        }

        /// <summary>Re-color every open document after a theme or accent switch.</summary>
        private void RefreshEditorThemes()
        {
            foreach (var p in new[] { LeftPane, RightPane })
                foreach (var t in p.Tabs)
                    t.Editor?.ApplyTheme();
        }

        // ═══════════════════════════════════════════════════════════
        //  KEYBOARD OWNERSHIP
        // ═══════════════════════════════════════════════════════════
        /// <summary>True while the caret is inside a document.</summary>
        /// <remarks>
        /// Walked up the tree rather than tested against one type, because the editor's find bar
        /// is a child of it: with a straight "is TextArea" test, typing in that bar would fall
        /// back to the window's own bindings and the first Backspace would navigate a folder.
        /// </remarks>
        internal bool EditorHasFocus
        {
            get
            {
                var d = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
                while (d != null)
                {
                    if (d is Editing.EditorControl) return true;
                    d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                      ? VisualTreeHelper.GetParent(d)
                      : LogicalTreeHelper.GetParent(d);
                }
                return false;
            }
        }

        /// <summary>
        /// Chords that belong to the WINDOW even while a document has focus. Everything not
        /// listed here reaches the editor.
        /// </summary>
        /// <remarks>
        /// The shell's list plus Ctrl+S and Ctrl+G. It reuses the shell's list because the two
        /// surfaces want exactly the same thing from the window - tabs, panes and overlays,
        /// nothing that touches text - and one list means a chord added for one cannot quietly
        /// go missing in the other. Those two are NOT in the shared list on purpose: over a pty
        /// Ctrl+S is XOFF, which would freeze the terminal with no obvious way back, and Ctrl+G
        /// is a bell the shell may well want to ring.
        /// </remarks>
        private bool IsEditorChord(System.Windows.Input.KeyEventArgs e, bool ctrl, bool shift, bool alt)
        {
            if (ctrl && !shift && !alt
                && (e.Key == System.Windows.Input.Key.S || e.Key == System.Windows.Input.Key.G))
                return true;

            return IsWindowChord(e, ctrl, shift, alt);
        }
    }
}
