using System.Collections.Generic;
using System.Linq;

namespace KillerFind.Models
{
    /// <summary>All matches found in a single file across all search terms.</summary>
    public class SearchResult
    {
        public string FilePath  { get; set; } = string.Empty;
        public string FileName  { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;

        public List<TermMatch> Matches { get; set; } = [];

        public int TotalMatchCount => Matches.Sum(m => m.Lines.Count > 0 ? m.Lines.Count : 1);
    }

    /// <summary>Matches for one SearchTerm within a file.</summary>
    public class TermMatch
    {
        public SearchTerm Term { get; set; } = null!;

        /// <summary>
        /// Populated for Content terms.
        /// Empty for FileName terms (the filename itself is the match).
        /// </summary>
        public List<LineMatch> Lines { get; set; } = [];
    }

    /// <summary>A single matched line — WPF-bindable (properties, not ValueTuple fields).</summary>
    public class LineMatch
    {
        public int    LineNumber { get; set; }
        public string LineText   { get; set; } = string.Empty;
    }
}
