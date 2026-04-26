using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using KillerFind.Models;

namespace KillerFind
{
    public class HtmlExporter
    {
        public void Export(string outputPath,
                           IList<SearchResult> results,
                           IList<SearchTerm>   terms,
                           string              rootPath)
        {
            var sb = new StringBuilder();
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int totalFiles   = results.Count;
            int totalMatches = results.Sum(r => r.TotalMatchCount);

            sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8""/>
<meta name=""viewport"" content=""width=device-width,initial-scale=1""/>
<title>KillerFind — Results</title>
<style>
  @import url('https://fonts.googleapis.com/css2?family=Source+Code+Pro:wght@400;600&display=swap');
  :root {
    --bg:        #1c1c1c;
    --surface:   #222222;
    --panel:     #1a1a1a;
    --sink:      #141414;
    --border:    #2e2e2e;
    --green:     #1ea54c;
    --green-dim: #0c5c2a;
    --fg:        #e8e8e8;
    --fg-dim:    #666666;
  }
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background: var(--bg);
    color: var(--fg);
    font-family: 'Source Code Pro', 'Cascadia Code', Consolas, monospace;
    font-size: 13px;
    line-height: 1.5;
  }

  /* ── Header ──────────────────────────────────── */
  header {
    background: var(--sink);
    border-bottom: 1px solid var(--border);
    padding: 14px 24px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .logo { color: var(--green); font-size: 20px; font-weight: 600; }
  .logo span { color: var(--fg); }
  .meta { color: var(--fg-dim); font-size: 11px; text-align: right; }
  .meta strong { color: var(--green); }

  /* ── Summary bar ─────────────────────────────── */
  .summary {
    background: var(--panel);
    border-bottom: 1px solid var(--border);
    padding: 10px 24px;
    display: flex;
    gap: 24px;
    flex-wrap: wrap;
    align-items: center;
  }
  .summary-item { font-size: 11px; color: var(--fg-dim); }
  .summary-item strong { color: var(--green); }

  /* ── Terms legend ────────────────────────────── */
  .terms-bar {
    background: var(--sink);
    border-bottom: 1px solid var(--border);
    padding: 8px 24px;
    display: flex;
    gap: 10px;
    flex-wrap: wrap;
    align-items: center;
  }
  .term-chip {
    border: 1px solid var(--green-dim);
    color: var(--green-dim);
    padding: 2px 8px;
    font-size: 10px;
    border-radius: 2px;
  }
  .term-chip.content { border-color: #3a6a50; color: #3a6a50; }

  /* ── Main layout ─────────────────────────────── */
  main { padding: 16px 24px; }

  /* ── Result card ─────────────────────────────── */
  .result-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-left: 3px solid var(--green-dim);
    margin-bottom: 10px;
    transition: border-left-color .15s;
  }
  .result-card:hover { border-left-color: var(--green); }

  .result-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 12px;
    background: var(--panel);
    border-bottom: 1px solid var(--border);
    cursor: pointer;
    user-select: none;
  }
  .result-header:hover { background: #1e1e1e; }
  .file-path { color: var(--fg); font-size: 12px; word-break: break-all; }
  .file-dir  { color: var(--fg-dim); font-size: 11px; }
  .match-badge {
    background: var(--green-dim);
    color: var(--green);
    padding: 2px 8px;
    font-size: 10px;
    white-space: nowrap;
    flex-shrink: 0;
    margin-left: 12px;
  }

  /* ── Match groups ────────────────────────────── */
  .match-groups { padding: 10px 12px; }
  .match-group { margin-bottom: 10px; }
  .match-group:last-child { margin-bottom: 0; }

  .term-label {
    font-size: 10px;
    color: var(--green-dim);
    border-bottom: 1px solid var(--border);
    padding-bottom: 4px;
    margin-bottom: 6px;
  }
  .term-label .mode { color: var(--fg-dim); }

  .fn-match {
    color: var(--green);
    font-size: 11px;
    padding: 2px 0;
  }

  .line-match {
    display: grid;
    grid-template-columns: 48px 1fr;
    gap: 0 8px;
    font-size: 11px;
    padding: 1px 0;
    border-bottom: 1px solid #222;
  }
  .line-match:last-child { border-bottom: none; }
  .line-num { color: var(--fg-dim); text-align: right; padding-right: 8px; border-right: 1px solid var(--border); }
  .line-text { color: var(--fg); white-space: pre-wrap; word-break: break-all; }

  /* ── Footer ──────────────────────────────────── */
  footer {
    border-top: 1px solid var(--border);
    padding: 10px 24px;
    color: var(--fg-dim);
    font-size: 10px;
    text-align: right;
  }
  footer a { color: var(--green-dim); text-decoration: none; }
</style>
</head>
<body>

<header>
  <div class=""logo"">&gt;_<span>KillerFind</span></div>
  <div class=""meta"">
    Generated " + H(ts) + @"<br/>
    Root: <strong>" + H(rootPath) + @"</strong>
  </div>
</header>

<div class=""summary"">
  <div class=""summary-item"">Files matched: <strong>" + totalFiles + @"</strong></div>
  <div class=""summary-item"">Total matches: <strong>" + totalMatches + @"</strong></div>
  <div class=""summary-item"">Terms: <strong>" + terms.Count(t => !string.IsNullOrWhiteSpace(t.Pattern)) + @"</strong></div>
</div>

<div class=""terms-bar"">
");
            foreach (var t in terms.Where(t => !string.IsNullOrWhiteSpace(t.Pattern)))
            {
                string cls = t.Mode == SearchTerm.SearchMode.Content ? "term-chip content" : "term-chip";
                string lbl = t.Mode == SearchTerm.SearchMode.FileName ? "F" : "C";
                sb.Append($"  <span class=\"{cls}\">[{lbl}] {H(t.Pattern)}</span>\n");
            }
            sb.Append(@"</div>

<main>
");
            foreach (var r in results.OrderBy(r => r.FilePath))
            {
                sb.Append($@"
<div class=""result-card"">
  <div class=""result-header"">
    <div>
      <div class=""file-path"">&#128196; {H(r.FileName)}</div>
      <div class=""file-dir"">{H(r.Directory)}</div>
    </div>
    <div class=""match-badge"">{r.TotalMatchCount} match{(r.TotalMatchCount == 1 ? "" : "es")}</div>
  </div>
  <div class=""match-groups"">
");
                foreach (var m in r.Matches)
                {
                    string modeLabel = m.Term.Mode == SearchTerm.SearchMode.FileName ? "F" : "C";
                    sb.Append($"    <div class=\"match-group\">\n");
                    sb.Append($"      <div class=\"term-label\"><span class=\"mode\">[{modeLabel}]</span> {H(m.Term.Pattern)}</div>\n");

                    if (m.Lines.Count == 0)
                    {
                        // Filename match
                        sb.Append($"      <div class=\"fn-match\">&#10003; filename match</div>\n");
                    }
                    else
                    {
                        foreach (var lm in m.Lines)
                        {
                            sb.Append($"      <div class=\"line-match\">" +
                                      $"<span class=\"line-num\">{lm.LineNumber}</span>" +
                                      $"<span class=\"line-text\">{H(lm.LineText)}</span></div>\n");
                        }
                    }

                    sb.Append("    </div>\n");
                }

                sb.Append("  </div>\n</div>\n");
            }

            sb.Append(@"
</main>
<footer>
  v0.1.0 &nbsp;&nbsp; &copy; 2026 &nbsp; <a href=""https://thekiller.net"">Steve the Killer</a>
</footer>

</body>
</html>
");
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        private static string H(string s) => WebUtility.HtmlEncode(s);
    }
}
