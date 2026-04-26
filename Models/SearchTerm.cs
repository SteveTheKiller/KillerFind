using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KillerFind.Models
{
    public class SearchTerm : INotifyPropertyChanged
    {
        public enum SearchMode { FileName, Content }

        private SearchMode _mode = SearchMode.FileName;
        public SearchMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                Notify();
                Notify(nameof(ModeLabel));
                Notify(nameof(ModeTooltip));
            }
        }

        public string ModeLabel   => Mode == SearchMode.FileName ? "F" : "C";
        public string ModeTooltip => Mode == SearchMode.FileName
            ? "Filename / wildcard  (e.g. *.log)"
            : "Content search  (text inside files)";

        private string _pattern = string.Empty;
        public string Pattern
        {
            get => _pattern;
            set { _pattern = value; Notify(); }
        }

        // -1 = not yet searched, ≥0 = match count from last run
        private int _matchCount = -1;
        public int MatchCount
        {
            get => _matchCount;
            set
            {
                _matchCount = value;
                Notify();
                Notify(nameof(MatchBadge));
            }
        }

        public string MatchBadge => _matchCount < 0 ? string.Empty : $"({_matchCount})";

        public void ResetCount() => MatchCount = -1;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
