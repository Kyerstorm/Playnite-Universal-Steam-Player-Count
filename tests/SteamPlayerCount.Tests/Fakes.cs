using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using SteamPlayerCount.Core;

namespace SteamPlayerCount.Tests
{
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow + by;
        }
    }

    internal sealed class FakeSearch : ISteamSearch
    {
        public int Calls;
        public Func<string, SearchOutcome> Handler = term => SearchOutcome.Success(new SteamCandidate[0]);
        public Action BeforeReturn;

        public Task<SearchOutcome> SearchAsync(string term, CancellationToken ct)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            var outcome = Handler(term);
            BeforeReturn?.Invoke();
            return Task.FromResult(outcome);
        }
    }

    internal static class Games
    {
        public static readonly Guid GogPluginId = new Guid("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e");

        public static GameInfo Steam(string libraryGameId, string name = "Steam Game")
        {
            return new GameInfo(Guid.NewGuid(), name, AppIdResolver.SteamLibraryPluginId, libraryGameId, null);
        }

        public static GameInfo Gog(string name, params string[] links)
        {
            return new GameInfo(Guid.NewGuid(), name, GogPluginId, "gog-1", links);
        }
    }

    internal sealed class FakeCounts : ISteamPlayerCounts
    {
        public int Calls;
        public Func<int, CountOutcome> Handler = appId => CountOutcome.Success(100);
        public Task Gate = Task.CompletedTask;

        public async Task<CountOutcome> GetAsync(int appId, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Gate.ConfigureAwait(false);
            return Handler(appId);
        }
    }
}
