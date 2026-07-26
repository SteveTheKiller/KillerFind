using System;
using System.IO;
using System.Linq;
using System.Windows;

// Export: CSV for spreadsheets, HTML for the styled report (HtmlExporter.cs builds
// the report itself). Partial of MainWindow. Column headers stay English
// (machine-readable) per project convention.
namespace KillerFind
{
    public partial class MainWindow
    {
        internal void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var tab = _active;
            if (tab.Results.Count == 0)
            {
                SetTabStatusKey(tab, "Str_Status_NothingExport");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "CSV File|*.csv",
                FileName = $"KillerFind-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                Title    = "Save results as CSV"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Name,Folder,SizeBytes,Modified,Found");
                foreach (var r in tab.Results)
                {
                    string found = string.Join("; ", r.Matches.Select(m =>
                        m.Lines.Count > 0 ? $"{m.Term.ModeName} ({m.Lines.Count})" : m.Term.ModeName));
                    sb.AppendLine(string.Join(",",
                        Csv(r.FileName), Csv(r.Directory), r.SizeBytes.ToString(),
                        Csv(r.ModifiedLabel), Csv(found)));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                SetTabStatusKey(tab, "Str_Status_Exported", dlg.FileName);
            }
            catch (Exception ex)
            {
                SetTabStatusKey(tab, "Str_Status_ExportFailed", ex.Message);
            }
        }

        private static string Csv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";

        internal void Export_Click(object sender, RoutedEventArgs e)
        {
            var tab = _active;
            if (tab.Results.Count == 0)
            {
                SetTabStatusKey(tab, "Str_Status_NothingExport");
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
                    new HtmlExporter().Export(dlg.FileName, tab.Results,
                        [.. tab.Groups.SelectMany(g => g.Terms)], Pane.RootPathBox.Text);
                    SetTabStatusKey(tab, "Str_Status_Exported", dlg.FileName);
                    System.Diagnostics.Process.Start(dlg.FileName);
                }
                catch (Exception ex)
                {
                    SetTabStatusKey(tab, "Str_Status_ExportFailed", ex.Message);
                }
            }
        }
    }
}
