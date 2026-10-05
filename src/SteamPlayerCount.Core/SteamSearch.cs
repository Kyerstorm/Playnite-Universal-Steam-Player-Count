using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum SearchStatus
    {
        Success,
        Failed,
        RateLimited,
    }

    public sealed class SearchOutcome
    {
        private static readonly IReadOnlyList<SteamCandidate> None = new SteamCandidate[0];

        private SearchOutcome(SearchStatus status, IReadOnlyList<SteamCandidate> candidates)
        {
            Status = status;
            Candidates = candidates;
        }

        public SearchStatus Status { get; }
        public IReadOnlyList<SteamCandidate> Candidates { get; }

        public static SearchOutcome Success(IEnumerable<SteamCandidate> candidates)
        {
            return new SearchOutcome(SearchStatus.Success, candidates == null ? None : candidates.ToList());
        }

        public static SearchOutcome Failed()
        {
            return new SearchOutcome(SearchStatus.Failed, None);
        }

        public static SearchOutcome RateLimited()
        {
            return new SearchOutcome(SearchStatus.RateLimited, None);
        }
    }

    public interface ISteamSearch
    {
        Task<SearchOutcome> SearchAsync(string term, CancellationToken ct);
    }
}
