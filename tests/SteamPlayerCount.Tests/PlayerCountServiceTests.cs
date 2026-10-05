using System;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class PlayerCountServiceTests
    {
        private readonly FakeClock clock = new FakeClock();
        private readonly FakeCounts counts = new FakeCounts();
        private readonly FakeSearch search = new FakeSearch();

        private PlayerCountService Service()
        {
            var resolver = new AppIdResolver(new MatchStore(), search, clock, () => true);
            return new PlayerCountService(resolver, counts, clock);
        }

        [Fact]
        public async Task Returns_the_count_for_a_resolved_game()
        {
            counts.Handler = appId => CountOutcome.Success(495050);

            var result = await Service().GetAsync(Games.Steam("570", "Dota 2"), CancellationToken.None);

            Assert.True(result.HasCount);
            Assert.Equal(495050, result.PlayerCount);
            Assert.Equal(570, result.AppId);
            Assert.Equal("Dota 2", result.SteamName);
        }

        [Fact]
        public async Task An_unresolved_game_has_no_count_and_no_fetch()
        {
            var result = await Service().GetAsync(Games.Gog("Obscure"), CancellationToken.None);

            Assert.False(result.HasCount);
            Assert.Equal(0, counts.Calls);
        }

        [Fact]
        public async Task A_count_is_cached_per_app_for_120_seconds()
        {
            var service = Service();

            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(119));
            await service.GetAsync(Games.Gog("Dota 2", "https://store.steampowered.com/app/570/"), CancellationToken.None);
            Assert.Equal(1, counts.Calls);

            clock.Advance(TimeSpan.FromSeconds(1));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task No_data_is_cached_for_ten_minutes()
        {
            counts.Handler = appId => CountOutcome.NoData();
            var service = Service();

            Assert.False((await service.GetAsync(Games.Steam("570"), CancellationToken.None)).HasCount);
            clock.Advance(TimeSpan.FromMinutes(9));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(1, counts.Calls);

            clock.Advance(TimeSpan.FromMinutes(1));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task A_failed_fetch_is_not_cached()
        {
            counts.Handler = appId => CountOutcome.Failed();
            var service = Service();

            Assert.False((await service.GetAsync(Games.Steam("570"), CancellationToken.None)).HasCount);
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);

            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task A_throwing_client_gives_no_count_instead_of_an_exception()
        {
            counts.Handler = appId => { throw new InvalidOperationException("boom"); };

            var result = await Service().GetAsync(Games.Steam("570"), CancellationToken.None);

            Assert.False(result.HasCount);
        }

        [Fact]
        public async Task Concurrent_requests_for_the_same_app_share_one_fetch()
        {
            var gate = new TaskCompletionSource<bool>();
            counts.Gate = gate.Task;
            var service = Service();

            var first = service.GetAsync(Games.Steam("570"), CancellationToken.None);
            var second = service.GetAsync(Games.Steam("570"), CancellationToken.None);
            gate.SetResult(true);
            var results = await Task.WhenAll(first, second);

            Assert.True(results[0].HasCount);
            Assert.True(results[1].HasCount);
            Assert.Equal(1, counts.Calls);
        }

        [Fact]
        public async Task A_cancelled_caller_stops_waiting_but_the_fetch_still_fills_the_cache()
        {
            var gate = new TaskCompletionSource<bool>();
            counts.Gate = gate.Task;
            var service = Service();
            var cts = new CancellationTokenSource();

            var waiting = service.GetAsync(Games.Steam("570"), cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

            gate.SetResult(true);
            counts.Gate = Task.CompletedTask;
            PlayerCountResult result = PlayerCountResult.None;
            for (var i = 0; i < 50 && !result.HasCount; i++)
            {
                await Task.Delay(20);
                result = await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            }

            Assert.True(result.HasCount);
            Assert.Equal(1, counts.Calls);
        }

        [Fact]
        public async Task A_null_game_has_no_count()
        {
            Assert.False((await Service().GetAsync(null, CancellationToken.None)).HasCount);
        }
    }
}
