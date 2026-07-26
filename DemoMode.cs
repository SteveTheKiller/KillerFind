using System;
using System.Collections.Generic;
using System.Linq;
using KillerFind.Models;

// --demo / /demo: fabricated tabs and results for marketing screenshots, so captures
// never leak real file names or folder structures. Also hides the install badge, and
// makes the About card render its signed state (publisher, thumbprint and the AKA line -
// see About.cs) so a capture from an unsigned local build matches the released one.
// Partial of MainWindow (KillerScan's DemoMode.cs pattern).
namespace KillerFind
{
    public partial class MainWindow
    {
        public static bool DemoMode;

        private static readonly Random DemoRng = new(1337);   // same data every run

        private void GenerateDemoData()
        {
            var placeholders = _tabs.ToList();   // the blank startup tab(s)

            // ── Tab 1: classic name search - a year of invoices ──────────────────
            var t1 = CreateTab();
            t1.Title    = "~\\Documents";
            t1.RootPath = @"C:\Users\steve\Documents";
            t1.Groups[0].Terms[0].Pattern = "invoice";
            t1.QueryLabel = "name: invoice";
            string inv = @"C:\Users\steve\Documents\Invoices";
            for (int m = 0; m < 12; m++)
            {
                var date = new DateTime(2025, 7, 1).AddMonths(m).AddDays(DemoRng.Next(3, 25));
                AddDemoResult(t1, inv, $"invoice_{date:yyyy-MM}.pdf", 40 + DemoRng.Next(220), date);
            }
            AddDemoResult(t1, @"C:\Users\steve\Documents", "invoice_template.docx", 28, new DateTime(2025, 9, 3));
            AddDemoResult(t1, @"C:\Users\steve\Documents\Archive", "old invoices.zip", 4096 + DemoRng.Next(2048), new DateTime(2024, 12, 30));
            FinishDemoTab(t1, 8412, 1.87);

            // ── Tab 2: content search with a filter row showing ──────────────────
            var t2 = CreateTab();
            t2.Title    = "~\\code";
            t2.RootPath = @"C:\Users\steve\code";
            t2.Groups[0].Terms[0].Mode    = SearchTerm.SearchMode.Content;
            t2.Groups[0].Terms[0].Pattern = "TODO";
            t2.Filters.Add(new SearchFilter { FieldIndex = SearchFilter.FieldExt, Text = "ps1" });
            t2.QueryLabel = "content: TODO  |  extension is ps1";
            string scripts = @"C:\Users\steve\code\killer-scripts";
            AddDemoResult(t2, scripts, "Backup-Nightly.ps1", 12, new DateTime(2026, 5, 14), new List<LineMatch>
            {
                new() { LineNumber = 42,  LineText = "# TODO: skip locked files instead of retrying forever" },
                new() { LineNumber = 118, LineText = "# TODO: email the report when the share is unreachable" },
            });
            AddDemoResult(t2, scripts, "Deploy-Agent.ps1", 9, new DateTime(2026, 6, 2), new List<LineMatch>
            {
                new() { LineNumber = 77, LineText = "# TODO: pull the tenant list from the API" },
            });
            AddDemoResult(t2, scripts, "Get-StaleProfiles.ps1", 6, new DateTime(2026, 3, 21), new List<LineMatch>
            {
                new() { LineNumber = 14, LineText = "# TODO: exclude service accounts" },
                new() { LineNumber = 31, LineText = "# TODO: make the age threshold a parameter" },
            });
            AddDemoResult(t2, @"C:\Users\steve\code\homelab", "Rotate-Certs.ps1", 8, new DateTime(2026, 1, 9), new List<LineMatch>
            {
                new() { LineNumber = 5, LineText = "# TODO: wire up the renewal webhook" },
            });
            if (t2.Results.Count > 0) t2.Results[0].IsExpanded = true;   // show off line matches
            FinishDemoTab(t2, 23907, 4.32);

            // ── Tab 3: a piped search - drill into tab 1's results ───────────────
            var t3 = CreateTab();
            t3.Title     = "~\\Documents > invoice";
            t3.RootPath  = t1.RootPath;
            t3.PipeFiles = t1.Results.Select(r => r.FilePath).ToList();
            t3.PipeArgs  = [t1.Results.Count.ToString("N0"), t1.Title, "name: invoice"];
            t3.PipeLabel = string.Format(Loc("Str_Pipe_Scope"), t3.PipeArgs);
            t3.Groups[0].Terms[0].Pattern = "2026";
            t3.QueryLabel = "name: 2026";
            foreach (var r in t1.Results.Where(r => r.FileName.Contains("2026")))
                AddDemoResult(t3, r.Directory, r.FileName, (int)(r.SizeBytes / 1024), r.Modified);
            FinishDemoTab(t3, t1.Results.Count, 0.02);

            foreach (var old in placeholders) _tabs.Remove(old);
            UpdateTabBar();
            ActivateTab(t1);
        }

        private void AddDemoResult(SearchTab t, string folder, string name, int sizeKb,
                                   DateTime modified, List<LineMatch>? lines = null)
        {
            var term = t.Groups[0].Terms[0];
            var r = new SearchResult
            {
                FileName  = name,
                Directory = folder,
                FilePath  = System.IO.Path.Combine(folder, name),
                SizeBytes = sizeKb * 1024L + DemoRng.Next(1024),
                Modified  = modified,
                Seq       = t.Results.Count,
            };
            r.Matches.Add(new TermMatch { Term = term, Lines = lines ?? new List<LineMatch>() });
            if (term.MatchCount < 0) term.MatchCount = 0;
            term.MatchCount += lines is { Count: > 0 } ? lines.Count : 1;
            t.Results.Add(r);
        }

        private void FinishDemoTab(SearchTab t, int scanned, double seconds)
        {
            t.ScannedCount  = scanned;
            t.StatusKey     = "Str_Status_Done";
            t.StatusArgs    = [seconds.ToString("0.00"), t.Results.Count];
            t.StatsLabel    = string.Format(Loc("Str_Count_Matches"), t.Results.Count.ToString("N0"));
            t.ScannedLabel  = string.Format(Loc("Str_Status_Scanned"), scanned.ToString("N0"));
            t.StatusMessage = string.Format(Loc("Str_Status_Done"),
                seconds.ToString("0.00"), t.Results.Count);
        }
    }
}
