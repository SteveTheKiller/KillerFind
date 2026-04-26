using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KillerFind.Models;

namespace KillerFind
{
    public class SearchEngine
    {
        // ── Events ───────────────────────────────────────────────
        public event Action<List<SearchResult>>? ResultsBatch;   // flushed every ~150 ms
        public event Action<string>?             StatusChanged;
        public event Action<int>?                ProgressChanged; // files scanned so far

        // ── Public entry point ───────────────────────────────────
        public async Task SearchAsync(
            string           rootPath,
            IList<SearchTerm> terms,
            string           includePatterns,   // e.g. "*.txt;*.log"
            string           excludePatterns,   // e.g. "bin;obj;*.min.js"
            bool             caseSensitive,
            CancellationToken ct)
        {
            await Task.Run(() => RunSearch(rootPath, terms, includePatterns,
                                           excludePatterns, caseSensitive, ct), ct);
        }

        // ── Core search loop ─────────────────────────────────────
        private void RunSearch(
            string           rootPath,
            IList<SearchTerm> terms,
            string           includePatterns,
            string           excludePatterns,
            bool             caseSensitive,
            CancellationToken ct)
        {
            var includes = ParsePatterns(includePatterns);
            var excludes = ParsePatterns(excludePatterns);
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            // Stream files lazily — never ToList() the full tree, which blocks
            // indefinitely and eats RAM on large roots like C:\Users\steve.
            int processed = 0;
            var pending   = new List<SearchResult>();

            // Single timer gates all UI pushes (status, progress, result batches).
            // Nothing hits the dispatcher more than once every 150 ms.
            var uiTimer = System.Diagnostics.Stopwatch.StartNew();
            const int UiIntervalMs = 150;

            foreach (var filePath in SafeEnumerateFiles(rootPath))
            {
                ct.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(filePath);

                if (IsExcluded(filePath, excludes)) { ++processed; continue; }

                if (includes.Count > 0 && !IsIncluded(fileName, includes))
                { ++processed; continue; }

                ++processed;
                if (uiTimer.ElapsedMilliseconds >= UiIntervalMs)
                {
                    if (pending.Count > 0)
                    {
                        ResultsBatch?.Invoke(pending);
                        pending = new List<SearchResult>();
                    }
                    StatusChanged?.Invoke(filePath);
                    ProgressChanged?.Invoke(processed);
                    uiTimer.Restart();
                }

                var result = new SearchResult
                {
                    FilePath  = filePath,
                    FileName  = fileName,
                    Directory = Path.GetDirectoryName(filePath) ?? string.Empty
                };

                bool anyMatch = false;

                foreach (var term in terms)
                {
                    if (string.IsNullOrWhiteSpace(term.Pattern)) continue;

                    if (term.Mode == SearchTerm.SearchMode.FileName)
                    {
                        if (MatchesWildcard(fileName, term.Pattern, caseSensitive))
                        {
                            result.Matches.Add(new TermMatch { Term = term });
                            anyMatch = true;
                        }
                    }
                    else
                    {
                        var lines = SearchContent(filePath, term.Pattern, comparison);
                        if (lines.Count > 0)
                        {
                            result.Matches.Add(new TermMatch { Term = term, Lines = lines });
                            anyMatch = true;
                        }
                    }
                }

                if (anyMatch)
                    pending.Add(result);
            }

            // Flush any remaining results after the loop
            if (pending.Count > 0)
                ResultsBatch?.Invoke(pending);
        }

        // ── File enumeration ─────────────────────────────────────
        private static IEnumerable<string> SafeEnumerateFiles(string root)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();
                IEnumerable<string> files = Enumerable.Empty<string>();

                try { files = Directory.EnumerateFiles(dir); }
                catch { /* skip inaccessible */ }

                foreach (var f in files)
                    yield return f;

                IEnumerable<string> subdirs = Enumerable.Empty<string>();
                try { subdirs = Directory.EnumerateDirectories(dir); }
                catch { /* skip inaccessible */ }

                foreach (var d in subdirs)
                    queue.Enqueue(d);
            }
        }

        // ── Content search ───────────────────────────────────────
        private static List<LineMatch> SearchContent(
            string filePath, string pattern, StringComparison comparison)
        {
            var matches = new List<LineMatch>();
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                              FileShare.Read, bufferSize: 65536);

                // Skip likely binary files — null byte in first 4 KB
                var buf  = new byte[Math.Min(4096, fs.Length)];
                int read = fs.Read(buf, 0, buf.Length);
                for (int i = 0; i < read; i++)
                    if (buf[i] == 0) return matches;
                fs.Seek(0, SeekOrigin.Begin);

                // Stream line-by-line — never loads the whole file into memory
                using var reader = new StreamReader(fs, Encoding.UTF8,
                                                    detectEncodingFromByteOrderMarks: true,
                                                    bufferSize: 65536, leaveOpen: false);
                string? line;
                int lineNum = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    if (line.IndexOf(pattern, comparison) >= 0)
                        matches.Add(new LineMatch { LineNumber = lineNum, LineText = line.Trim() });
                }
            }
            catch { /* unreadable file — skip */ }
            return matches;
        }

        // ── Pattern helpers ──────────────────────────────────────
        private static List<string> ParsePatterns(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(';')
                      .Select(p => p.Trim())
                      .Where(p => p.Length > 0)
                      .ToList();
        }

        private static bool IsExcluded(string filePath, List<string> excludes)
        {
            string fileName = Path.GetFileName(filePath);
            foreach (var exc in excludes)
            {
                // Segment match (e.g. "bin", "obj", "node_modules")
                if (filePath.IndexOf(Path.DirectorySeparatorChar + exc + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                // Wildcard against filename
                if (MatchesWildcard(fileName, exc, false))
                    return true;
            }
            return false;
        }

        private static bool IsIncluded(string fileName, List<string> includes)
        {
            foreach (var inc in includes)
                if (MatchesWildcard(fileName, inc, false)) return true;
            return false;
        }

        public static bool MatchesWildcard(string input, string pattern, bool caseSensitive)
        {
            string regex = "^" + Regex.Escape(pattern)
                               .Replace("\\*", ".*")
                               .Replace("\\?", ".") + "$";
            var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(input, regex, opts);
        }
    }
}
