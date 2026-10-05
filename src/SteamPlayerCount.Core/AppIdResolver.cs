using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum ResolveStatus
    {
        Resolved,
        NoMatch,
        Failed,
        RateLimited,
    }

    public sealed class ResolveResult
    {
        public static readonly ResolveResult NoMatch = new ResolveResult(ResolveStatus.NoMatch, 0, null);
        public static readonly ResolveResult Failed = new ResolveResult(ResolveStatus.Failed, 0, null);
        public static readonly ResolveResult RateLimited = new ResolveResult(ResolveStatus.RateLimited, 0, null);

        private ResolveResult(ResolveStatus status, int appId, string steamName)
        {
            Status = status;
            AppId = appId;
            SteamName = steamName;
        }

        public ResolveStatus Status { get; }
        public int AppId { get; }
        public string SteamName { get; }

        public static ResolveResult Resolved(int appId, string steamName)
        {
            return new ResolveResult(ResolveStatus.Resolved, appId, steamName);
        }
    }

    public sealed class AppIdResolver
    {
        public static readonly Guid SteamLibraryPluginId = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        public static readonly TimeSpan NotFoundRetryAfter = TimeSpan.FromDays(30);

        private static readonly Regex SteamLinkRegex = new Regex(
            @"https?://(?:store\.steampowered|steamcommunity)\.com/app/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly MatchStore store;
        private readonly ISteamSearch search;
        private readonly IClock clock;
        private readonly Func<bool> nonSteamMatchingEnabled;

        public AppIdResolver(MatchStore store, ISteamSearch search, IClock clock, Func<bool> nonSteamMatchingEnabled)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.search = search ?? throw new ArgumentNullException(nameof(search));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.nonSteamMatchingEnabled = nonSteamMatchingEnabled ?? throw new ArgumentNullException(nameof(nonSteamMatchingEnabled));
        }

        public bool NeedsSearch(GameInfo game)
        {
            ResolveResult ignored;
            return game != null && !TryResolveLocal(game, out ignored);
        }

        public async Task<ResolveResult> ResolveAsync(GameInfo game, CancellationToken ct)
        {
            if (game == null)
            {
                return ResolveResult.NoMatch;
            }

            ResolveResult local;
            if (TryResolveLocal(game, out local))
            {
                return local;
            }

            var outcome = await search.SearchAsync(game.Name, ct).ConfigureAwait(false);
            if (outcome.Status == SearchStatus.RateLimited)
            {
                return ResolveResult.RateLimited;
            }

            if (outcome.Status != SearchStatus.Success)
            {
                return ResolveResult.Failed;
            }

            // The user may have chosen by hand while the search was running.
            MatchEntry current;
            ResolveResult manual;
            if (store.TryGet(game.Id, out current) && TryResolveManual(current, out manual))
            {
                return manual;
            }

            var match = StrictNameMatcher.FindMatch(game.Name, outcome.Candidates);
            store.Set(game.Id, new MatchEntry
            {
                AppId = match == null ? (int?)null : match.AppId,
                SteamName = match == null ? null : match.Name,
                Source = match == null ? MatchSource.NotFound : MatchSource.NameSearch,
                CheckedUtc = clock.UtcNow,
            });

            return match == null ? ResolveResult.NoMatch : ResolveResult.Resolved(match.AppId, match.Name);
        }

        private static bool TryResolveManual(MatchEntry entry, out ResolveResult result)
        {
            if (entry.Source == MatchSource.Manual && entry.AppId.HasValue)
            {
                result = ResolveResult.Resolved(entry.AppId.Value, entry.SteamName);
                return true;
            }

            if (entry.Source == MatchSource.ManualExcluded)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            result = null;
            return false;
        }

        private bool TryResolveLocal(GameInfo game, out ResolveResult result)
        {
            MatchEntry entry;
            var hasEntry = store.TryGet(game.Id, out entry);

            // 1. Manual choice.
            if (hasEntry && TryResolveManual(entry, out result))
            {
                return true;
            }

            // 2. Steam library game.
            int appId;
            if (game.LibraryPluginId == SteamLibraryPluginId && TryParseAppId(game.LibraryGameId, out appId))
            {
                result = ResolveResult.Resolved(appId, game.Name);
                return true;
            }

            if (!nonSteamMatchingEnabled())
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // 3. Steam link in the game's links.
            if (TryGetLinkAppId(game.LinkUrls, out appId))
            {
                result = ResolveResult.Resolved(appId, game.Name);
                return true;
            }

            // 4. Saved search result.
            if (hasEntry && entry.Source == MatchSource.NameSearch && entry.AppId.HasValue)
            {
                result = ResolveResult.Resolved(entry.AppId.Value, entry.SteamName);
                return true;
            }

            if (hasEntry && entry.Source == MatchSource.NotFound && clock.UtcNow - entry.CheckedUtc < NotFoundRetryAfter)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // A name with nothing comparable can never match, so do not search for it.
            if (GameNameNormalizer.Normalize(game.Name).Length == 0)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // 5. Needs a search.
            result = null;
            return false;
        }

        private static bool TryGetLinkAppId(IReadOnlyList<string> urls, out int appId)
        {
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var match = SteamLinkRegex.Match(url);
                if (match.Success && TryParseAppId(match.Groups[1].Value, out appId))
                {
                    return true;
                }
            }

            appId = 0;
            return false;
        }

        private static bool TryParseAppId(string text, out int appId)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out appId) && appId > 0;
        }
    }
}
