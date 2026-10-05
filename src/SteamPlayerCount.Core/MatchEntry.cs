using System;

namespace SteamPlayerCount.Core
{
    // Persisted to matches.json. Never renumber or reuse a value.
    public enum MatchSource
    {
        Manual = 1,
        NameSearch = 2,
        NotFound = 3,
        ManualExcluded = 4,
    }

    public sealed class MatchEntry
    {
        public int? AppId { get; set; }
        public string SteamName { get; set; }
        public MatchSource Source { get; set; }
        public DateTime CheckedUtc { get; set; }
    }
}
