using System;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public sealed class PluginServices : IDisposable
    {
        private static readonly TimeSpan SearchGap = TimeSpan.FromSeconds(1.5);

        private readonly SteamHttp http;
        private readonly MatchFile matchFile;

        public PluginServices(string dataPath, Func<bool> nonSteamMatchingEnabled, Action<Exception, string> logError)
        {
            Clock = new SystemClock();
            http = new SteamHttp();
            matchFile = new MatchFile(dataPath, Clock, logError);
            Store = matchFile.Load();
            matchFile.Attach(Store);
            Search = new SteamStoreSearchClient(http, new MinIntervalGate(SearchGap, Clock));
            Resolver = new AppIdResolver(Store, Search, Clock, nonSteamMatchingEnabled);
            Counts = new PlayerCountService(Resolver, new SteamPlayerCountsClient(http), Clock);
        }

        public IClock Clock { get; }
        public MatchStore Store { get; }
        public ISteamSearch Search { get; }
        public AppIdResolver Resolver { get; }
        public PlayerCountService Counts { get; }

        public void Dispose()
        {
            matchFile.Dispose();
            http.Dispose();
        }
    }
}
