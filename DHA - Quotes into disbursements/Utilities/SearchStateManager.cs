using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DHA.DSTC.WPF.Utilities
{
    public class PinnedItem
    {
        public Guid Id { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string Client { get; set; }
    }

    public class SearchState
    {
        public List<PinnedItem> PinnedProjects { get; set; } = new List<PinnedItem>();
        public List<PinnedItem> PinnedQuotes { get; set; } = new List<PinnedItem>();
        public List<string> RecentProjectSearches { get; set; } = new List<string>();
        public List<string> RecentQuoteSearches { get; set; } = new List<string>();
    }

    /// <summary>
    /// Manages per-user pinned jobs and recent search history.
    /// State is persisted in %AppData%\DHA\search_state.json.
    /// </summary>
    public class SearchStateManager
    {
        private const int MaxRecentSearches = 8;
        private const int MaxPinnedItems = 10;

        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DHA", "search_state.json");

        private SearchState _state;

        public IReadOnlyList<PinnedItem> PinnedProjects => _state.PinnedProjects;
        public IReadOnlyList<PinnedItem> PinnedQuotes => _state.PinnedQuotes;
        public IReadOnlyList<string> RecentProjectSearches => _state.RecentProjectSearches;
        public IReadOnlyList<string> RecentQuoteSearches => _state.RecentQuoteSearches;

        public SearchStateManager()
        {
            Load();
        }

        // ── Recent searches ──────────────────────────────────────────────

        public void AddRecentProjectSearch(string term)
        {
            term = term?.Trim();
            if (string.IsNullOrWhiteSpace(term)) return;
            _state.RecentProjectSearches.RemoveAll(s => s.Equals(term, StringComparison.OrdinalIgnoreCase));
            _state.RecentProjectSearches.Insert(0, term);
            while (_state.RecentProjectSearches.Count > MaxRecentSearches)
                _state.RecentProjectSearches.RemoveAt(_state.RecentProjectSearches.Count - 1);
            Save();
        }

        public void AddRecentQuoteSearch(string term)
        {
            term = term?.Trim();
            if (string.IsNullOrWhiteSpace(term)) return;
            _state.RecentQuoteSearches.RemoveAll(s => s.Equals(term, StringComparison.OrdinalIgnoreCase));
            _state.RecentQuoteSearches.Insert(0, term);
            while (_state.RecentQuoteSearches.Count > MaxRecentSearches)
                _state.RecentQuoteSearches.RemoveAt(_state.RecentQuoteSearches.Count - 1);
            Save();
        }

        // ── Pinned projects ───────────────────────────────────────────────

        public bool IsProjectPinned(Guid id) => _state.PinnedProjects.Any(p => p.Id == id);

        public void PinProject(PinnedItem item)
        {
            if (item == null || IsProjectPinned(item.Id)) return;
            _state.PinnedProjects.Insert(0, item);
            while (_state.PinnedProjects.Count > MaxPinnedItems)
                _state.PinnedProjects.RemoveAt(_state.PinnedProjects.Count - 1);
            Save();
        }

        public void UnpinProject(Guid id)
        {
            _state.PinnedProjects.RemoveAll(p => p.Id == id);
            Save();
        }

        public void TogglePinProject(PinnedItem item)
        {
            if (item == null) return;
            if (IsProjectPinned(item.Id))
                UnpinProject(item.Id);
            else
                PinProject(item);
        }

        // ── Pinned quotes ─────────────────────────────────────────────────

        public bool IsQuotePinned(Guid id) => _state.PinnedQuotes.Any(q => q.Id == id);

        public void PinQuote(PinnedItem item)
        {
            if (item == null || IsQuotePinned(item.Id)) return;
            _state.PinnedQuotes.Insert(0, item);
            while (_state.PinnedQuotes.Count > MaxPinnedItems)
                _state.PinnedQuotes.RemoveAt(_state.PinnedQuotes.Count - 1);
            Save();
        }

        public void UnpinQuote(Guid id)
        {
            _state.PinnedQuotes.RemoveAll(q => q.Id == id);
            Save();
        }

        public void TogglePinQuote(PinnedItem item)
        {
            if (item == null) return;
            if (IsQuotePinned(item.Id))
                UnpinQuote(item.Id);
            else
                PinQuote(item);
        }

        // ── Persistence ───────────────────────────────────────────────────

        private void Load()
        {
            try
            {
                if (File.Exists(StoragePath))
                {
                    var json = File.ReadAllText(StoragePath);
                    _state = JsonConvert.DeserializeObject<SearchState>(json) ?? new SearchState();
                    // Null-safety for lists (handles old file versions)
                    _state.PinnedProjects = _state.PinnedProjects ?? new List<PinnedItem>();
                    _state.PinnedQuotes = _state.PinnedQuotes ?? new List<PinnedItem>();
                    _state.RecentProjectSearches = _state.RecentProjectSearches ?? new List<string>();
                    _state.RecentQuoteSearches = _state.RecentQuoteSearches ?? new List<string>();
                }
                else
                {
                    _state = new SearchState();
                }
            }
            catch
            {
                _state = new SearchState();
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(StoragePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(StoragePath, JsonConvert.SerializeObject(_state, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchStateManager.Save failed: {ex.Message}");
            }
        }
    }
}
