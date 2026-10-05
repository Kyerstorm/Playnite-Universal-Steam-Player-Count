using System;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class AppIdResolverTests
    {
        private readonly MatchStore store = new MatchStore();
        private readonly FakeSearch search = new FakeSearch();
        private readonly FakeClock clock = new FakeClock();
        private bool nonSteamEnabled = true;

        private AppIdResolver Resolver()
        {
            return new AppIdResolver(store, search, clock, () => nonSteamEnabled);
        }

        private MatchEntry Entry(MatchSource source, int? appId)
        {
            return new MatchEntry { AppId = appId, SteamName = "Saved", Source = source, CheckedUtc = clock.UtcNow };
        }

        private void SearchReturns(params SteamCandidate[] candidates)
        {
            search.Handler = term => SearchOutcome.Success(candidates);
        }

        [Fact]
        public async Task Step1_manual_match_wins_over_everything()
        {
            var game = Games.Steam("570");
            store.Set(game.Id, Entry(MatchSource.Manual, 999));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(999, result.AppId);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step1_manual_exclusion_means_no_match_even_with_a_link()
        {
            var game = Games.Gog("Prey", "https://store.steampowered.com/app/480490/Prey/");
            store.Set(game.Id, Entry(MatchSource.ManualExcluded, null));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step2_steam_library_game_uses_its_own_id()
        {
            var result = await Resolver().ResolveAsync(Games.Steam("570", "Dota 2"), CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(570, result.AppId);
            Assert.Equal("Dota 2", result.SteamName);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step2_still_works_when_non_steam_matching_is_disabled()
        {
            nonSteamEnabled = false;

            var result = await Resolver().ResolveAsync(Games.Steam("570"), CancellationToken.None);

            Assert.Equal(570, result.AppId);
        }

        [Theory]
        [InlineData("https://store.steampowered.com/app/480490/Prey/")]
        [InlineData("http://steamcommunity.com/app/480490")]
        [InlineData("HTTPS://STORE.STEAMPOWERED.COM/app/480490")]
        public async Task Step3_steam_link_gives_the_id_without_storing_anything(string url)
        {
            var game = Games.Gog("Prey", "https://www.gog.com/game/prey", url);

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(480490, result.AppId);
            Assert.Equal(0, search.Calls);
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public async Task Step4_saved_search_match_is_reused()
        {
            var game = Games.Gog("Prey");
            store.Set(game.Id, Entry(MatchSource.NameSearch, 480490));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal("Saved", result.SteamName);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step4_recent_not_found_is_not_searched_again()
        {
            var game = Games.Gog("Obscure");
            store.Set(game.Id, Entry(MatchSource.NotFound, null));
            clock.Advance(TimeSpan.FromDays(29));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step4_not_found_older_than_30_days_is_searched_again()
        {
            var game = Games.Gog("Prey");
            store.Set(game.Id, Entry(MatchSource.NotFound, null));
            clock.Advance(TimeSpan.FromDays(30));
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal(1, search.Calls);
        }

        [Fact]
        public async Task Step5_search_hit_is_stored_as_NameSearch()
        {
            var game = Games.Gog("Prey");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"), new SteamCandidate(865670, "Prey - Mooncrash", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(480490, result.AppId);
            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(MatchSource.NameSearch, saved.Source);
            Assert.Equal(480490, saved.AppId);
            Assert.Equal("Prey", saved.SteamName);
            Assert.Equal(clock.UtcNow, saved.CheckedUtc);
        }

        [Fact]
        public async Task Step5_search_miss_is_stored_as_NotFound()
        {
            var game = Games.Gog("Obscure");

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(MatchSource.NotFound, saved.Source);
            Assert.Null(saved.AppId);
        }

        [Theory]
        [InlineData(SearchStatus.Failed, ResolveStatus.Failed)]
        [InlineData(SearchStatus.RateLimited, ResolveStatus.RateLimited)]
        public async Task Step5_a_failed_search_stores_nothing(SearchStatus searchStatus, ResolveStatus expected)
        {
            var game = Games.Gog("Prey");
            search.Handler = term => searchStatus == SearchStatus.Failed ? SearchOutcome.Failed() : SearchOutcome.RateLimited();

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(expected, result.Status);
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public async Task Disabled_non_steam_matching_skips_links_saved_matches_and_search()
        {
            nonSteamEnabled = false;
            var withLink = Games.Gog("Prey", "https://store.steampowered.com/app/480490/");
            var withSaved = Games.Gog("Prey");
            store.Set(withSaved.Id, Entry(MatchSource.NameSearch, 480490));

            Assert.Equal(ResolveStatus.NoMatch, (await Resolver().ResolveAsync(withLink, CancellationToken.None)).Status);
            Assert.Equal(ResolveStatus.NoMatch, (await Resolver().ResolveAsync(withSaved, CancellationToken.None)).Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Cancellation_propagates_and_stores_nothing()
        {
            var game = Games.Gog("Prey");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver().ResolveAsync(game, cts.Token));
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public void NeedsSearch_is_true_only_when_local_steps_are_inconclusive()
        {
            var resolver = Resolver();
            var saved = Games.Gog("Saved");
            store.Set(saved.Id, Entry(MatchSource.NameSearch, 1));

            Assert.True(resolver.NeedsSearch(Games.Gog("Prey")));
            Assert.False(resolver.NeedsSearch(Games.Steam("570")));
            Assert.False(resolver.NeedsSearch(Games.Gog("Prey", "https://store.steampowered.com/app/480490/")));
            Assert.False(resolver.NeedsSearch(saved));
            Assert.False(resolver.NeedsSearch(null));
        }

        [Fact]
        public async Task Concurrent_resolves_of_the_same_game_share_one_search()
        {
            var game = Games.Gog("Prey");
            var gate = new TaskCompletionSource<bool>();
            search.Gate = gate.Task;
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));
            var resolver = Resolver();

            var first = resolver.ResolveAsync(game, CancellationToken.None);
            var second = resolver.ResolveAsync(game, CancellationToken.None);
            gate.SetResult(true);
            var results = await Task.WhenAll(first, second);

            Assert.Equal(480490, results[0].AppId);
            Assert.Equal(480490, results[1].AppId);
            Assert.Equal(1, search.Calls);
        }

        // Review Focus 1
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("???")]
        public async Task A_name_with_nothing_comparable_is_no_match_without_a_search(string name)
        {
            var game = Games.Gog(name);

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
            Assert.Empty(store.Snapshot());
        }

        // Review Focus 2
        [Theory]
        [InlineData(MatchSource.Manual)]
        [InlineData(MatchSource.ManualExcluded)]
        public async Task A_manual_choice_made_during_the_search_is_not_overwritten(MatchSource manualSource)
        {
            var game = Games.Gog("Prey");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));
            search.BeforeReturn = () => store.Set(game.Id, Entry(manualSource, manualSource == MatchSource.Manual ? (int?)777 : null));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(manualSource, saved.Source);
            if (manualSource == MatchSource.Manual)
            {
                Assert.Equal(777, result.AppId);
            }
            else
            {
                Assert.Equal(ResolveStatus.NoMatch, result.Status);
            }
        }

        // Review Focus 3
        [Theory]
        [InlineData("not-a-number")]
        [InlineData("99999999999999999999")]
        [InlineData("-5")]
        [InlineData("0")]
        [InlineData(null)]
        public async Task A_steam_library_game_with_an_unusable_id_falls_through_to_the_search(string libraryGameId)
        {
            var game = Games.Steam(libraryGameId, "Some Mod");

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(1, search.Calls);
        }

        // Review Focus 3
        [Fact]
        public async Task Null_empty_and_malformed_links_are_ignored()
        {
            var game = Games.Gog("Prey", null, "", "not a url", "https://store.steampowered.com/app/abc/", "https://store.steampowered.com/app/99999999999999999999/");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal(1, search.Calls);
        }
    }
}
